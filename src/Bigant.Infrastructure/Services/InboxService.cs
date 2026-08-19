using Bigant.Domain;
using System.Text.Json;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bigant.Infrastructure.Services;

/// <summary>Bizga kelgan hujjat — Didox'dan ham, qo'lda ham shu yo'ldan o'tadi.</summary>
public sealed record IncomingDocument(
    DocType DocType,
    string? DidoxId,
    string? DocNumber,
    DateOnly DocDate,
    string? SenderInn,
    string? SenderName,
    decimal Amount,
    decimal VatAmount,
    string RawJson,
    /// <summary>In — bizga keldi, Out — biz yubordik (Didox outbox).</summary>
    DocDirection Direction = DocDirection.In);

public interface IInboxService
{
    /// <summary>
    /// Kelgan hujjatni ro'yxatga oladi, shartnoma kartochkasiga ilaydi
    /// va eslatma chiqaradi. Takroriy chaqiruvda hech narsa qilmaydi.
    /// </summary>
    Task<Document?> RegisterAsync(IncomingDocument inc, CancellationToken ct = default);
}

public sealed class InboxService : IInboxService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notify;
    private readonly INumberGenerator _numbers;
    private readonly IAuditService _audit;
    private readonly ILogger<InboxService> _log;

    public InboxService(AppDbContext db, INotificationService notify, INumberGenerator numbers,
                        IAuditService audit, ILogger<InboxService> log)
    {
        _db = db; _notify = notify; _numbers = numbers; _audit = audit; _log = log;
    }

    public async Task<Document?> RegisterAsync(IncomingDocument inc, CancellationToken ct = default)
    {
        // Dublikat himoyasi — Didox ID bo'yicha.
        if (!string.IsNullOrWhiteSpace(inc.DidoxId) &&
            await _db.Documents.AnyAsync(d => d.DidoxId == inc.DidoxId, ct))
            return null;

        var sender = await ResolveSenderAsync(inc, ct);

        // Kelgan hujjat qaysi xarid bitimiga tegishli — topamiz yoki ochamiz.
        var deal = await FindOrCreateDealAsync(inc, sender, ct);

        // Kelgan hujjat ham zanjirga ulanadi: postavshik yuborgan faktura
        // qaysi doverennost/dogovor asosida ekanini ko'rsatish uchun.
        var prior = await _db.Documents
            .Where(d => d.DealId == deal.Id
                     && d.Status != DocStatus.Cancelled && d.Status != DocStatus.Rejected)
            .Select(d => new { d.DocType, d.Id })
            .ToListAsync(ct);

        var priorChain = prior.Select(p => (p.DocType, p.Id)).ToList();

        var doc = new Document
        {
            DealId = deal.Id,
            DocType = inc.DocType,
            Direction = inc.Direction,
            Status = DocStatus.Delivered,
            DocNumber = inc.DocNumber,
            DocDate = inc.DocDate,
            CounterpartyId = sender?.Id,
            Amount = inc.Amount,
            VatAmount = inc.VatAmount,
            DidoxId = inc.DidoxId,
            // Zanjir: avval Didox'ning O'Z havolasi, bo'lmasa tartib bo'yicha taxmin.
            ParentDocumentId = await ResolveParentAsync(inc, deal.Id, priorChain, ct),
            Payload = SafeParse(inc.RawJson)
        };
        // DOVERENNOST MUDDATI — Didox javobidan o'qiladi.
        // Ilgari bu ma'lumot biz doverennost chiqarganda to'lardi. Endi hujjat
        // Didox'da yaratiladi, shuning uchun muddatni xom JSON dan olamiz —
        // busiz «muddat tugayapti» ogohlantirishi umuman ishlamaydi.
        if (inc.DocType == DocType.Doverennost)
            doc.Doverennost = BuildDoverennostInfo(inc);

        _db.Documents.Add(doc);
        await _db.SaveChangesAsync(ct);

        // Cheklistdagi mos qadamni yopamiz — qo'lda belgilash kerak emas.
        if (DealWorkflow.StepForDocument(inc.DocType) is { } stepType)
        {
            var step = await _db.DealSteps
                .FirstOrDefaultAsync(s => s.DealId == deal.Id && s.Step == stepType, ct);
            if (step is { Status: StepStatus.Pending })
            {
                step.Status = StepStatus.Done;
                step.CompletedAt = DateTimeOffset.UtcNow;
                step.DocumentId = doc.Id;
                await _db.SaveChangesAsync(ct);
            }
        }

        // ── KIRUVCHI HUJJAT ESLATMASI ──
        // Bugalter «kimdan, nima, QAYSI HUJJAT UCHUN» ekanini darhol ko'rsin.
        var label = DealWorkflow.DocLabel(inc.DocType);
        var who = sender?.Name ?? inc.SenderName ?? "Noma'lum";

        // MATN QURISH HIMOYALANGAN: bu yerda kutilmagan xato chiqsa hujjat
        // bazada qolib, bugalter hech narsa bilmay qolardi. Endi eng yomon
        // holatda ham qisqa xabar baribir ketadi.
        // (2026-08-18: aynan shunday bo'lgan — X-ESF-900 xabari yo'qolgan.)
        string body;
        try
        {
            body = await BuildIncomingBodyAsync(inc, doc, deal, who, priorChain, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Eslatma matnini qurishda xato — qisqa shakl yuboriladi");
            body = $"{who} — №{inc.DocNumber ?? "—"}\nShartnoma: {deal.Number}";
        }

        await _notify.RaiseAsync(new Notification
        {
            Kind = NotificationKind.IncomingDocument,
            TargetRole = UserRole.Perehod,
            Severity = inc.DocType == DocType.Contract
                ? NotificationSeverity.Warning       // dogovor — e'tibor talab qiladi
                : NotificationSeverity.Info,
            UserId = deal.ManagerId,
            DealId = deal.Id,
            DocumentId = doc.Id,
            Title = $"{DocIcon(inc.DocType)} {label.ToUpperInvariant()} " +
                    (inc.Direction == DocDirection.Out ? "YUBORILDI" : "KELDI"),
            Body = body,
            DedupKey = $"Incoming:doc:{doc.Id}"
        }, ct);

        // ── SUMMA MOS KELMADI — darhol, hujjat kelgan zahoti ──
        // Faktura summasi TTN yoki dogovor summasidan farq qilsa, bu yo
        // qisman yetkazib berish, yo xato. Ikkalasi ham tekshirishga arziydi.
        if (doc.ParentDocumentId is { } parentId && inc.Amount > 0)
        {
            var parent = await _db.Documents.AsNoTracking()
                .Where(d => d.Id == parentId)
                .Select(d => new { d.DocType, d.DocNumber, d.Amount })
                .FirstOrDefaultAsync(ct);

            if (parent is not null && parent.Amount > 0
                && Math.Abs(parent.Amount - inc.Amount) > 1000m)
            {
                var diff = inc.Amount - parent.Amount;
                await _notify.RaiseAsync(new Notification
                {
                    Kind = NotificationKind.AmountMismatch,
                    TargetRole = UserRole.Perehod,
                    Severity = NotificationSeverity.Warning,
                    DealId = deal.Id,
                    DocumentId = doc.Id,
                    Title = "⚠️ SUMMA MOS KELMADI",
                    Body = $"Kontragent: {who}\n" +
                           $"Shartnoma:  {deal.Number}\n\n" +
                           $"{label} №{inc.DocNumber ?? "—"}: {inc.Amount:N0}\n" +
                           $"{DealWorkflow.DocLabel(parent.DocType)} №{parent.DocNumber}: {parent.Amount:N0}\n" +
                           $"Farq: {(diff > 0 ? "+" : "")}{diff:N0} so'm\n\n" +
                           "Qisman yetkazib berish yoki xato bo'lishi mumkin.",
                    DedupKey = $"Mismatch:doc:{doc.Id}"
                }, ct);
            }
        }

        // ── HUJJAT KELGAN ZAHOTI QILINADIGAN TEKSHIRUVLAR ──
        // Har biri ALOHIDA xabar chiqaradi: bitta hujjatda ikki muammo bo'lsa,
        // ikkita xabar keladi. Aralashtirib yuborsak, bugalter ikkinchisini
        // ko'rmay qolardi.
        await RunDocumentChecksAsync(inc, doc, deal, who, priorChain, ct);

        _audit.Write(null, nameof(Document), doc.Id, "inbox",
            null, new { inc.DidoxId, inc.DocNumber, Type = inc.DocType.ToString() });
        await _db.SaveChangesAsync(ct);

        // Didox'dan hujjat kelgan zahoti xabar ketsin — kutish yo'q.
        await _notify.PushPendingAsync(ct);

        _log.LogInformation("Kiruvchi hujjat ro'yxatga olindi: {Type} №{Number}", inc.DocType, inc.DocNumber);
        return doc;
    }

    /// <summary>
    /// O'zbekistonda QQS stavkasi. Faktura QQS si shu nisbatdan keskin
    /// chetlashsa — hujjatda xato bor yoki stavka boshqacha.
    /// </summary>
    private const decimal VatRate = 0.12m;

    /// <summary>
    /// Hujjat kelgan zahoti bajariladigan yettita tekshiruv.
    ///
    /// Hammasi Didox hujjatining O'ZIDAGI maydonlarga tayanadi — turi, raqami,
    /// sanasi, INN, summasi, QQS va doverennost muddati. Qo'shimcha so'rov
    /// yubormaydi, shuning uchun hujjat kelishini sekinlashtirmaydi.
    ///
    /// Har biri alohida xabar chiqaradi va o'z roliga yo'naltiriladi.
    /// </summary>
    private async Task RunDocumentChecksAsync(
        IncomingDocument inc, Document doc, Deal deal, string who,
        IReadOnlyList<(DocType DocType, long Id)> prior, CancellationToken ct)
    {
        var docLabel = DealWorkflow.DocLabel(inc.DocType);
        var no = inc.DocNumber ?? "—";
        var head = $"Kontragent: {who}\nHujjat:     {docLabel} №{no}\n\n";

        // ── 1. TAKRORIY HUJJAT RAQAMI ──
        // Bitta kontragent bir xil raqamni ikki marta yuborsa, bu yo qayta
        // yuborish, yo xato. Ikkalasi ham hisobni buzadi.
        if (!string.IsNullOrWhiteSpace(inc.DocNumber) && doc.CounterpartyId is { } cpId)
        {
            var twin = await _db.Documents.AsNoTracking()
                .Where(d => d.Id != doc.Id
                         && d.CounterpartyId == cpId
                         && d.DocType == inc.DocType
                         && d.DocNumber == inc.DocNumber)
                .Select(d => new { d.Id, d.DocDate })
                .FirstOrDefaultAsync(ct);

            if (twin is not null)
                await _notify.RaiseAsync(new Notification
                {
                    Kind = NotificationKind.DuplicateDocNumber,
                    TargetRole = UserRole.Perehod,
                    Severity = NotificationSeverity.Warning,
                    DealId = deal.Id, DocumentId = doc.Id,
                    Title = "🔁 BIR XIL RAQAMLI HUJJAT",
                    Body = head +
                           $"Shu raqamli hujjat allaqachon bor.\n" +
                           $"Eskisining sanasi: {twin.DocDate:dd.MM.yyyy}\n" +
                           $"Yangisining sanasi: {inc.DocDate:dd.MM.yyyy}\n\n" +
                           "Qayta yuborilgan yoki raqam xato bo'lishi mumkin.",
                    DedupKey = $"DupNo:doc:{doc.Id}"
                }, ct);
        }

        // ── 2. INN MOS EMAS ──
        // Hujjatdagi INN bitimdagi kontragentnikidan farq qilsa, hujjat
        // boshqa bitimga tegishli bo'lishi mumkin.
        if (!string.IsNullOrWhiteSpace(inc.SenderInn))
        {
            var dealInn = await _db.Counterparties.AsNoTracking()
                .Where(c => c.Id == deal.CounterpartyId)
                .Select(c => c.Inn)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(dealInn) && dealInn != inc.SenderInn)
                await _notify.RaiseAsync(new Notification
                {
                    Kind = NotificationKind.InnMismatch,
                    TargetRole = UserRole.Perehod,
                    Severity = NotificationSeverity.Critical,
                    DealId = deal.Id, DocumentId = doc.Id,
                    Title = "⚠️ INN MOS KELMADI",
                    Body = head +
                           $"Hujjatdagi INN:  {inc.SenderInn}\n" +
                           $"Bitimdagi INN:   {dealInn}\n\n" +
                           "Hujjat boshqa shartnomaga tegishli bo'lishi mumkin — tekshiring.",
                    DedupKey = $"InnMismatch:doc:{doc.Id}"
                }, ct);
        }

        // ── 3. ESKI SANALI HUJJAT ──
        // Sanasi kelgan kunidan 30 kundan ko'proq oldin bo'lsa, hujjat
        // kechikkan. Oy yopilgandan keyin kelgan faktura hisobni buzadi.
        var lateDays = UzTime.Today.DayNumber - inc.DocDate.DayNumber;
        if (lateDays > 30)
            await _notify.RaiseAsync(new Notification
            {
                Kind = NotificationKind.BackdatedDocument,
                TargetRole = UserRole.Perehod,
                Severity = NotificationSeverity.Warning,
                DealId = deal.Id, DocumentId = doc.Id,
                Title = "📅 ESKI SANALI HUJJAT",
                Body = head +
                       $"Hujjat sanasi: {inc.DocDate:dd.MM.yyyy}\n" +
                       $"Bugun keldi:   {UzTime.Today:dd.MM.yyyy}\n" +
                       $"Kechikish:     {lateDays} kun\n\n" +
                       "Oy yopilgan bo'lsa, hisobotni qayta ko'rish kerak.",
                DedupKey = $"Backdated:doc:{doc.Id}"
            }, ct);

        // ── 4-5. QQS TEKSHIRUVI (faqat fakturada) ──
        if (inc.DocType == DocType.Esf && inc.Amount > 0)
        {
            // QQS SIZ FAKTURA HAR DOIM XATO EMAS.
            // Kontragent soddalashtirilgan soliqda bo'lsa, u umuman QQS
            // ko'rsatmaydi va har fakturada ogohlantirish chiqarish — quruq
            // shovqin. Shuning uchun faqat SHU bitimda ilgari QQS bo'lgan
            // bo'lsa ogohlantiramiz: ya'ni kutilgan, lekin kelmagan.
            var vatExpected = await _db.Documents.AsNoTracking()
                .AnyAsync(d => d.DealId == deal.Id && d.Id != doc.Id && d.VatAmount > 0, ct);

            if (inc.VatAmount <= 0)
            {
                // Butunlay QQS siz ishlaydigan kontragent — jim o'tamiz.
                if (vatExpected)
                    await _notify.RaiseAsync(new Notification
                    {
                        Kind = NotificationKind.VatMissing,
                        TargetRole = UserRole.Realizatsiya,
                        Severity = NotificationSeverity.Warning,
                        DealId = deal.Id, DocumentId = doc.Id,
                        Title = "🧾 FAKTURADA QQS YO'Q",
                        Body = head +
                               $"Summa: {inc.Amount:N0} so'm\n" +
                               "QQS:   0\n\n" +
                               "Shu shartnoma bo'yicha oldingi hujjatlarda QQS bor edi.\n" +
                               // Kimning fakturasi ekani muhim: kelganini qaytartiramiz,
                               // o'zimiznikini esa o'zimiz tuzatamiz.
                               (inc.Direction == DocDirection.In
                                   ? "QQS ni hisobga ola olmaymiz — fakturani qaytartiring."
                                   : "Biz QQS siz faktura chiqardik — Didox'da tekshiring."),
                        DedupKey = $"VatMissing:doc:{doc.Id}"
                    }, ct);
            }
            else
            {
                // QQS odatda summaning ichida: soliqsiz baza × 12%.
                // Kutilganidan 5% dan ko'p chetlashsa — e'tibor beriladi.
                var expected = inc.Amount / (1 + VatRate) * VatRate;
                var drift = expected == 0 ? 0 : Math.Abs(inc.VatAmount - expected) / expected;

                if (drift > 0.05m)
                    await _notify.RaiseAsync(new Notification
                    {
                        Kind = NotificationKind.VatSuspicious,
                        TargetRole = UserRole.Realizatsiya,
                        Severity = NotificationSeverity.Warning,
                        DealId = deal.Id, DocumentId = doc.Id,
                        Title = "🧮 QQS SUMMASI SHUBHALI",
                        Body = head +
                               $"Summa:        {inc.Amount:N0} so'm\n" +
                               $"Fakturadagi QQS: {inc.VatAmount:N0} so'm\n" +
                               $"12% bo'yicha:    {expected:N0} so'm\n\n" +
                               "Stavka boshqacha yoki hujjatda xato bo'lishi mumkin.",
                        DedupKey = $"VatOdd:doc:{doc.Id}"
                    }, ct);
            }
        }

        // ── 6. MUDDATI O'TGAN DOVERENNOST ──
        if (inc.DocType == DocType.Doverennost)
        {
            var until = await _db.Doverennosti.AsNoTracking()
                .Where(x => x.DocumentId == doc.Id)
                .Select(x => x.ValidUntil)
                .FirstOrDefaultAsync(ct);

            if (until is { } u && u < UzTime.Today)
                await _notify.RaiseAsync(new Notification
                {
                    Kind = NotificationKind.DoverennostExpired,
                    TargetRole = UserRole.Perehod,
                    Severity = NotificationSeverity.Critical,
                    DealId = deal.Id, DocumentId = doc.Id,
                    Title = "⛔ DOVERENNOST MUDDATI O'TGAN",
                    Body = head +
                           $"Muddati tugagan: {u:dd.MM.yyyy}\n" +
                           $"Bugun:           {UzTime.Today:dd.MM.yyyy}\n\n" +
                           "Bunday doverennost bo'yicha tovar chiqarib bo'lmaydi.",
                    DedupKey = $"DovExpired:doc:{doc.Id}"
                }, ct);
        }

        // ── 7. ZANJIR TARTIBI BUZILDI ──
        // Faktura yoki TTN doverennostdan OLDIN kelgan bo'lsa, bu tartib
        // buzilishi: qaysi asosda tovar berilgani noaniq bo'lib qoladi.
        if (inc.DocType is DocType.Esf or DocType.Ttn
            && deal.Kind == DealKind.Purchase
            && !prior.Any(p => p.DocType == DocType.Doverennost))
        {
            await _notify.RaiseAsync(new Notification
            {
                Kind = NotificationKind.ChainOutOfOrder,
                TargetRole = UserRole.Perehod,
                Severity = NotificationSeverity.Warning,
                DealId = deal.Id, DocumentId = doc.Id,
                Title = "🔀 TARTIB BUZILDI",
                Body = head +
                       $"{docLabel} keldi, lekin doverennost hali yo'q.\n\n" +
                       "To'g'ri tartib: dogovor → doverennost → TTN → faktura.\n" +
                       "Doverennost chiqarilganmi — tekshiring.",
                DedupKey = $"OutOfOrder:doc:{doc.Id}"
            }, ct);
        }

        // ── 8. HUJJAT RAQAMSIZ ──
        // Raqamsiz hujjatni keyin topib bo'lmaydi va soliqda ko'rsatib bo'lmaydi.
        if (string.IsNullOrWhiteSpace(inc.DocNumber))
            await _notify.RaiseAsync(new Notification
            {
                Kind = NotificationKind.DocumentWithoutNumber,
                TargetRole = UserRole.Perehod,
                Severity = NotificationSeverity.Warning,
                DealId = deal.Id, DocumentId = doc.Id,
                Title = "🔢 HUJJAT RAQAMSIZ KELDI",
                Body = $"Kontragent: {who}\n" +
                       $"Hujjat turi: {docLabel}\n" +
                       $"Sana: {inc.DocDate:dd.MM.yyyy}\n" +
                       $"Summa: {inc.Amount:N0} so'm\n\n" +
                       "Raqamsiz hujjatni keyinchalik topib bo'lmaydi.\n" +
                       "Kontragentdan raqamini so'rang.",
                DedupKey = $"NoNumber:doc:{doc.Id}"
            }, ct);

        // ── 9. SUMMA NOL ──
        // Pul hujjatida summa bo'lmasa — hisobga olib bo'lmaydi.
        if (inc.Amount <= 0 && inc.DocType is DocType.Contract or DocType.Esf
                                           or DocType.InvoiceForPayment)
            await _notify.RaiseAsync(new Notification
            {
                Kind = NotificationKind.DocumentWithoutAmount,
                TargetRole = UserRole.Perehod,
                Severity = NotificationSeverity.Warning,
                DealId = deal.Id, DocumentId = doc.Id,
                Title = "0️⃣ HUJJATDA SUMMA YO'Q",
                Body = head + "Summa: 0\n\n" +
                       "Bu turdagi hujjatda summa bo'lishi kerak —\n" +
                       "qarz hisobiga kirmaydi.",
                DedupKey = $"NoAmount:doc:{doc.Id}"
            }, ct);

        // ── 10. SANASI KELAJAKDA ──
        if (inc.DocDate > UzTime.Today)
            await _notify.RaiseAsync(new Notification
            {
                Kind = NotificationKind.FutureDatedDocument,
                TargetRole = UserRole.Perehod,
                Severity = NotificationSeverity.Warning,
                DealId = deal.Id, DocumentId = doc.Id,
                Title = "🔮 HUJJAT SANASI KELAJAKDA",
                Body = head +
                       $"Hujjat sanasi: {inc.DocDate:dd.MM.yyyy}\n" +
                       $"Bugun:         {UzTime.Today:dd.MM.yyyy}\n\n" +
                       "Sana xato kiritilgan bo'lishi mumkin —\n" +
                       "hisobot noto'g'ri davrga tushadi.",
                DedupKey = $"Future:doc:{doc.Id}"
            }, ct);

        // ── 11. DOVERENNOSTDA ISHONCHLI SHAXS YO'Q ──
        // Ismsiz doverennost bilan tovar berilmaydi.
        if (inc.DocType == DocType.Doverennost)
        {
            var agent = await _db.Doverennosti.AsNoTracking()
                .Where(x => x.DocumentId == doc.Id)
                .Select(x => x.EmployeeName)
                .FirstOrDefaultAsync(ct);

            // «—» — ism topilmaganda qo'yiladigan belgi, ya'ni ism YO'Q.
            if (string.IsNullOrWhiteSpace(agent) || agent.Trim() == "—")
                await _notify.RaiseAsync(new Notification
                {
                    Kind = NotificationKind.DoverennostWithoutAgent,
                    TargetRole = UserRole.Perehod,
                    Severity = NotificationSeverity.Critical,
                    DealId = deal.Id, DocumentId = doc.Id,
                    Title = "👤 DOVERENNOSTDA ISHONCHLI SHAXS YO'Q",
                    Body = head +
                           "Kim tovarni olishi ko'rsatilmagan.\n\n" +
                           "Bunday doverennost bilan tovar berilmaydi —\n" +
                           "Didox'da to'ldirilganini tekshiring.",
                    DedupKey = $"NoAgent:doc:{doc.Id}"
                }, ct);
        }

        // ── 12. BIR XIL INN, BOSHQA NOM ──
        // Kontragent nomini o'zgartirgan yoki hujjatda xato bor.
        if (!string.IsNullOrWhiteSpace(inc.SenderInn) && !string.IsNullOrWhiteSpace(inc.SenderName)
            && doc.CounterpartyId is { } cpId2)
        {
            var known = await _db.Counterparties.AsNoTracking()
                .Where(c => c.Id == cpId2).Select(c => c.Name).FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(known)
                && !string.Equals(known.Trim(), inc.SenderName.Trim(), StringComparison.OrdinalIgnoreCase))
                await _notify.RaiseAsync(new Notification
                {
                    Kind = NotificationKind.CounterpartyNameChanged,
                    TargetRole = UserRole.Perehod,
                    Severity = NotificationSeverity.Info,
                    DealId = deal.Id, DocumentId = doc.Id,
                    Title = "🏢 KONTRAGENT NOMI O'ZGARDI",
                    Body = $"INN: {inc.SenderInn}\n\n" +
                           $"Bizdagi nomi:    {known}\n" +
                           $"Hujjatdagi nomi: {inc.SenderName}\n\n" +
                           "Tashkilot nomini o'zgartirgan bo'lishi mumkin.",
                    DedupKey = $"NameChanged:doc:{doc.Id}"
                }, ct);
        }

        // ── 13. BIR KUNDA BIR XIL HUJJAT ──
        if (doc.CounterpartyId is { } cpId3 && inc.Amount > 0)
        {
            var twin = await _db.Documents.AsNoTracking()
                .AnyAsync(d => d.Id != doc.Id
                            && d.CounterpartyId == cpId3
                            && d.DocType == inc.DocType
                            && d.DocDate == inc.DocDate
                            && d.Amount == inc.Amount, ct);

            if (twin)
                await _notify.RaiseAsync(new Notification
                {
                    Kind = NotificationKind.SameDayDuplicate,
                    TargetRole = UserRole.Perehod,
                    Severity = NotificationSeverity.Warning,
                    DealId = deal.Id, DocumentId = doc.Id,
                    Title = "👯 BIR KUNDA BIR XIL HUJJAT",
                    Body = head +
                           $"Sana:  {inc.DocDate:dd.MM.yyyy}\n" +
                           $"Summa: {inc.Amount:N0} so'm\n\n" +
                           "Shu kontragentdan xuddi shunday hujjat bugun allaqachon kelgan.\n" +
                           "Ikki marta yuborilgan bo'lishi mumkin.",
                    DedupKey = $"SameDay:doc:{doc.Id}"
                }, ct);
        }

        // ── 14. BIR KUNDA JUDA KO'P HUJJAT ──
        if (doc.CounterpartyId is { } cpId4)
        {
            var todayCount = await _db.Documents.AsNoTracking()
                .CountAsync(d => d.CounterpartyId == cpId4 && d.DocDate == inc.DocDate, ct);

            if (todayCount == 10)   // aynan chegaraga yetganda bir marta
                await _notify.RaiseAsync(new Notification
                {
                    Kind = NotificationKind.ManyDocsFromOne,
                    TargetRole = UserRole.Perehod,
                    Severity = NotificationSeverity.Info,
                    DealId = deal.Id,
                    Title = "📚 BITTA KONTRAGENTDAN KO'P HUJJAT",
                    Body = $"Kontragent: {who}\n\n" +
                           $"Bugun {todayCount} ta hujjat keldi ({inc.DocDate:dd.MM.yyyy}).\n\n" +
                           "Katta partiya bo'lishi mumkin — yoki takror yuborilgan.",
                    DedupKey = $"ManyDocs:cp:{cpId4}:{inc.DocDate:yyyyMMdd}"
                }, ct);
        }

        // ── 15. FAKTURALAR SHARTNOMA SUMMASIDAN OSHDI ──
        if (inc.DocType == DocType.Esf && inc.Amount > 0)
        {
            var esfTotal = await _db.Documents.AsNoTracking()
                .Where(d => d.DealId == deal.Id && d.DocType == DocType.Esf
                         && d.Status != DocStatus.Cancelled)
                .SumAsync(d => (decimal?)d.Amount, ct) ?? 0;

            if (deal.AmountTotal > 0 && esfTotal > deal.AmountTotal + 1000m)
                await _notify.RaiseAsync(new Notification
                {
                    Kind = NotificationKind.EsfOverContract,
                    TargetRole = UserRole.Realizatsiya,
                    Severity = NotificationSeverity.Warning,
                    DealId = deal.Id, DocumentId = doc.Id,
                    Title = "📈 FAKTURALAR SHARTNOMADAN OSHDI",
                    Body = head +
                           $"Shartnoma summasi:  {deal.AmountTotal:N0} so'm\n" +
                           $"Fakturalar jami:    {esfTotal:N0} so'm\n" +
                           $"Ortiqcha:           {esfTotal - deal.AmountTotal:N0} so'm\n\n" +
                           "Qo'shimcha kelishuv bormi yoki hujjatda xatomi — tekshiring.",
                    DedupKey = $"EsfOver:doc:{doc.Id}"
                }, ct);
        }
    }

    /// <summary>
    /// Didox javobidan doverennost tafsilotlarini yig'adi: ishonchli shaxs va
    /// AMAL QILISH MUDDATI. Muddat topilmasa 10 kun deb olinadi — O'zbekistonda
    /// odatiy muddat shu; bunda ogohlantirish baribir ishlaydi, faqat sana
    /// taxminiy bo'ladi va bugalter kartochkada to'g'rilay oladi.
    ///
    /// TODO(didox): maydon nomlari rasmiy hujjatdan tasdiqlansin.
    /// </summary>
    private static DoverennostInfo BuildDoverennostInfo(IncomingDocument inc)
    {
        var (name, pinfl, until) = ReadEmpowerment(inc.RawJson);

        return new DoverennostInfo
        {
            EmployeeName = name ?? "—",
            EmployeePinfl = pinfl ?? "",
            ValidUntil = until ?? inc.DocDate.AddDays(10),
        };
    }

    private static (string? Name, string? Pinfl, DateOnly? Until) ReadEmpowerment(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson) || rawJson == "{}") return (null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            string? Find(string block, params string[] fields)
            {
                if (!root.TryGetProperty(block, out var b) || b.ValueKind != JsonValueKind.Object)
                    return null;
                foreach (var f in fields)
                    if (b.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.String)
                        return v.GetString();
                return null;
            }

            var until = Find("EmpowermentDoc", "DateOfExpire", "ExpireDate", "ValidUntil");
            var name = Find("Agent", "FullName", "Name");
            var pinfl = Find("Agent", "Pinfl", "PINFL");

            DateOnly? parsed = DateOnly.TryParse(until, out var d) ? d : null;
            return (name, pinfl, parsed);
        }
        catch (JsonException) { return (null, null, null); }
    }

    /// <summary>
    /// Har hujjat turining O'Z belgisi — bugalter xabarlar ro'yxatini
    /// aylantirganda o'qimasdan, faqat belgiga qarab ajratsin.
    /// </summary>
    private static string DocIcon(DocType t) => t switch
    {
        DocType.Contract          => "\U0001F4C4",   // varaq — dogovor
        DocType.InvoiceForPayment => "\U0001F4B5",   // pul — schyot na oplatu
        DocType.Doverennost       => "\U0001F4DD",   // qalam bilan yozuv — ishonchnoma
        DocType.Ttn               => "\U0001F69A",   // yuk mashinasi — TTN
        DocType.Esf               => "\U0001F9FE",   // chek — schyot-faktura
        _                         => "\U0001F4E5",   // kiruvchi quti
    };

    /// <summary>
    /// Kelgan hujjat qaysi hujjat ASOSIDA chiqarilgan.
    ///
    /// IKKI MANBA, birinchisi ishonchliroq:
    ///   1. DIDOX'NING O'Z HAVOLASI — fakturada `FacturaEmpowermentDoc`,
    ///      TTN'da shunga o'xshash blok bo'ladi. Yuboruvchi to'ldirgan bo'lsa,
    ///      bu ANIQ ma'lumot: aynan qaysi doverennost uchun ekani yozib qo'yilgan.
    ///   2. TARTIB BO'YICHA TAXMIN — zanjirdagi oldingi hujjat. Didox havolasi
    ///      bo'lmasa shu ishlatiladi. Oddiy oqimda to'g'ri, lekin bitta kontragent
    ///      bilan bir vaqtda ikkita yetkazib berish ketsa adashishi mumkin.
    /// </summary>
    private async Task<long?> ResolveParentAsync(
        IncomingDocument inc, long dealId,
        IReadOnlyList<(DocType DocType, long Id)> prior, CancellationToken ct)
    {
        // 1) Didox havolasi bo'yicha
        if (ExtractEmpowermentNumber(inc.RawJson) is { } dovNo)
        {
            var byRef = await _db.Documents.AsNoTracking()
                .Where(d => d.DealId == dealId
                         && d.DocType == DocType.Doverennost
                         && d.DocNumber == dovNo)
                .Select(d => (long?)d.Id)
                .FirstOrDefaultAsync(ct);

            if (byRef is not null)
            {
                _log.LogInformation(
                    "Zanjir Didox havolasidan olindi: {Doc} → doverennost №{No}",
                    inc.DocNumber, dovNo);
                return byRef;
            }

            _log.LogWarning(
                "Didox {Doc} hujjatida doverennost №{No} ko'rsatilgan, lekin bizda topilmadi",
                inc.DocNumber, dovNo);
        }

        // 2) Tartib bo'yicha
        return DealWorkflow.ResolveParent(inc.DocType, prior);
    }

    /// <summary>
    /// Didox javobidan doverennost raqamini ajratib oladi.
    ///
    /// TODO(didox): maydon nomlari RASMIY HUJJATDAN tasdiqlanishi kerak.
    /// Hozir ehtimoliy nomlar bo'yicha qidiriladi; topilmasa `null` qaytadi va
    /// zanjir tartib bo'yicha quriladi — ya'ni noto'g'ri nom hech narsani buzmaydi.
    /// </summary>
    private static string? ExtractEmpowermentNumber(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson) || rawJson == "{}") return null;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);

            // Ehtimoliy joylar: FacturaEmpowermentDoc.EmpowermentNo, EmpowermentDoc.No, ...
            string[] blocks = ["FacturaEmpowermentDoc", "EmpowermentDoc", "Empowerment"];
            string[] fields = ["EmpowermentNo", "No", "Number", "DocNo"];

            foreach (var b in blocks)
            {
                if (!doc.RootElement.TryGetProperty(b, out var block)) continue;
                if (block.ValueKind != JsonValueKind.Object) continue;

                foreach (var f in fields)
                    if (block.TryGetProperty(f, out var v) &&
                        v.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(v.GetString()))
                        return v.GetString();
            }
        }
        catch (JsonException) { }

        return null;
    }

    /// <summary>
    /// Eslatma matni: kimdan, qanday hujjat, QAYSI HUJJAT UCHUN.
    /// Egasining talabi — faktura/TTN kelganda aynan qaysi doverennost
    /// uchun ekani ko'rinsin. Zanjir faktura → TTN → doverennost bo'lgani
    /// uchun bevosita otasi TTN bo'lishi mumkin; doverennostni alohida topamiz.
    /// </summary>
    private async Task<string> BuildIncomingBodyAsync(
        IncomingDocument inc, Document doc, Deal deal, string who,
        IReadOnlyList<(DocType DocType, long Id)> prior, CancellationToken ct)
    {
        var basis = doc.ParentDocumentId is { } pid
            ? await _db.Documents.AsNoTracking()
                .Where(d => d.Id == pid)
                .Select(d => DealWorkflow.DocLabel(d.DocType) + " №" + d.DocNumber)
                .FirstOrDefaultAsync(ct)
            : null;

        string? dovLine = null;
        if (inc.DocType is DocType.Esf or DocType.Ttn)
        {
            var dov = prior.Where(p => p.DocType == DocType.Doverennost)
                           .Select(p => (long?)p.Id)
                           .LastOrDefault();

            if (dov is { } dovId)
            {
                var no = await _db.Documents.AsNoTracking()
                    .Where(d => d.Id == dovId)
                    .Select(d => d.DocNumber)
                    .FirstOrDefaultAsync(ct);
                dovLine = $"✅ Doverennost №{no ?? "—"} uchun";
            }
            else
            {
                // Doverennostsiz tovar kelgan — bugalter darhol bilsin.
                dovLine = "⚠️ DOVERENNOST YO'Q — tekshiring!";
            }
        }

        // Ko'rinish: kim → nima → qancha → qaysi asosda → qaysi shartnomada.
        // Har biri alohida qatorda, bir qarashda o'qiladi.
        var body = inc.Direction == DocDirection.Out ? $"Kimga:   {who}\n" : $"Kimdan:  {who}\n";
        body += $"Hujjat:  №{inc.DocNumber ?? "—"}\n";
        if (inc.Amount > 0) body += $"Summa:   {inc.Amount:N0} so'm\n";

        // QQS alohida qator bo'lib chiqadi — bugalter uni hujjatdan qidirib
        // o'tirmasin. Nol bo'lsa ko'rsatilmaydi: bo'sh qator ma'lumot bermaydi.
        if (inc.VatAmount > 0) body += $"QQS:     {inc.VatAmount:N0} so'm\n";

        // DOVERENNOST — eng muhimi MUDDAT. Egasining talabi shu edi:
        // muddati o'tib ketgan doverennost bo'yicha tovar chiqarib bo'lmaydi.
        // Shu sabab xabarning o'zida ko'rinadi, keyingi eslatmani kutmasdan.
        if (inc.DocType == DocType.Doverennost)
        {
            var info = await _db.Doverennosti.AsNoTracking()
                .Where(x => x.DocumentId == doc.Id)
                .Select(x => new { x.EmployeeName, x.ValidUntil })
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(info?.EmployeeName))
                body += $"Vakil:   {info!.EmployeeName}\n";

            if (info?.ValidUntil is { } until)
            {
                var left = until.DayNumber - UzTime.Today.DayNumber;
                var note = left < 0 ? "MUDDATI O'TGAN"
                         : left == 0 ? "bugun tugaydi"
                         : $"{left} kun qoldi";
                body += $"Muddat:  {until:dd.MM.yyyy} ({note})\n";
            }
        }

        if (dovLine is not null) body += $"\n{dovLine}\n";
        else if (basis is not null) body += $"\n✅ {basis} asosida\n";
        return body + $"\nShartnoma: {deal.Number}";
    }

    private async Task<Counterparty?> ResolveSenderAsync(IncomingDocument inc, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(inc.SenderInn))
        {
            var found = await _db.Counterparties.FirstOrDefaultAsync(c => c.Inn == inc.SenderInn, ct);
            if (found is not null)
            {
                if (!found.IsSupplier) { found.IsSupplier = true; await _db.SaveChangesAsync(ct); }
                return found;
            }

            // Yangi postavshik — avtomatik ochamiz, bugalter keyin to'ldiradi.
            var created = new Counterparty
            {
                Name = inc.SenderName ?? $"INN {inc.SenderInn}",
                Inn = inc.SenderInn,
                IsSupplier = true
            };
            _db.Counterparties.Add(created);
            await _db.SaveChangesAsync(ct);
            return created;
        }

        return string.IsNullOrWhiteSpace(inc.SenderName)
            ? null
            : await _db.Counterparties.FirstOrDefaultAsync(c => c.Name == inc.SenderName, ct);
    }

    private async Task<Deal> FindOrCreateDealAsync(IncomingDocument inc, Counterparty? sender,
        CancellationToken ct)
    {
        // DOGOVOR — yangi kelishuv, demak YANGI bitim.
        // Uni ochiq bitimga yopishtirsak ikkita boshqa-boshqa kelishuv bitta
        // kartochkada aralashib ketadi va summalar chalkashadi.
        // Qolgan hujjatlar (faktura, TTN, doverennost) mavjud bitimga ilinadi.
        if (sender is not null && inc.DocType != DocType.Contract)
        {
            // Bigant ham sotadi, ham sotib oladi — shuning uchun kelgan hujjat
            // sotuv bitimiga ham, xarid bitimiga ham tegishli bo'lishi mumkin.
            // Draft ham hisobga olinadi: sotuvchi buyurtma kiritgan, lekin hali
            // hech qanday hujjat ketmagan bo'lishi mumkin.
            var open = await _db.Deals
                .Where(d => d.CounterpartyId == sender.Id
                         && (d.Status == DealStatus.Active || d.Status == DealStatus.Draft))
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync(ct);

            // Doverennostni HAR DOIM xaridor chiqaradi.
            // Demak bizga doverennost kelgan bo'lsa — biz SOTUVCHIMIZ, bu SOTUV bitimi.
            // Bu qat'iy qoida: xarid bitimiga yopishtirish zanjirni buzadi
            // (2026-08-18 testda aynan shu bo'lgan — klient doverennosti
            //  xarid kartochkasiga tushib ketgan edi).
            // Doverennostni HAR DOIM xaridor chiqaradi — shuning uchun yo'nalish
            // bitim turini aniq belgilaydi:
            //   bizga KELDI  → xaridor ular → bu bizning SOTUVimiz
            //   biz YUBORDIK → xaridor biz  → bu XARID
            // Yo'nalishni hisobga olmasak doverennost noto'g'ri bitimga tushadi
            // va zanjir buziladi (2026-08-19 testda aynan shu bo'lgan).
            List<Deal> candidates;
            if (inc.DocType == DocType.Doverennost)
            {
                var want = inc.Direction == DocDirection.Out ? DealKind.Purchase : DealKind.Sale;
                candidates = open.Where(d => d.Kind == want).ToList();

                // Mos bitim topilmasa — boshqasiga YOPISHTIRMAYMIZ, yangisini ochamiz.
                if (candidates.Count == 0)
                    return await CreateDealAsync(inc, sender.Id, want, ct);
            }
            else
            {
                var want = inc.Direction == DocDirection.Out ? DealKind.Sale : DealKind.Purchase;
                candidates = open.Where(d => d.Kind == want).ToList();
                if (candidates.Count == 0) candidates = open;
            }

            // Bitta kontragentda bir necha ochiq bitim bo'lishi mumkin.
            // Eng ishonchli belgi — SUMMA mos kelishi (1000 so'm farqqa yo'l qo'yamiz).
            // Bo'lmasa — eng oxirgisi.
            var match = (inc.Amount > 0
                    ? candidates.FirstOrDefault(d => Math.Abs(d.AmountTotal - inc.Amount) <= 1000m)
                    : null)
                ?? candidates.FirstOrDefault();

            // Faktura kelgan bo'lsa va bitim summasi hali belgilanmagan bo'lsa —
            // hujjatdagi summani olamiz (bitim kiruvchi hujjatdan ochilgan).
            if (match is not null)
            {
                if (inc.DocType == DocType.Esf && match.AmountTotal == 0 && inc.Amount > 0)
                {
                    match.AmountTotal = inc.Amount;
                    await _db.SaveChangesAsync(ct);
                }
                return match;
            }
        }

        // Yo'q bo'lsa — yangi xarid kartochkasi ochiladi, cheklist bilan.
        var cpId = sender?.Id ?? await EnsureUnknownSupplierAsync(ct);
        return await CreateDealAsync(inc, cpId, DealKind.Purchase, ct);
    }

    /// <summary>Kiruvchi hujjatdan yangi bitim ochadi — cheklisti bilan.</summary>
    private async Task<Deal> CreateDealAsync(IncomingDocument inc, long counterpartyId,
        DealKind kind, CancellationToken ct)
    {
        var date = inc.DocDate;
        var deal = new Deal
        {
            Kind = kind,
            Number = await _numbers.NextAsync(kind == DealKind.Sale ? "S" : "P", ct),
            CounterpartyId = counterpartyId,
            Status = DealStatus.Active,
            ContractDate = date,
            AmountTotal = inc.Amount,
            Comment = "Kiruvchi hujjatdan avtomatik ochildi"
        };
        deal.Steps.AddRange(DealWorkflow.BuildSteps(kind, date));

        _db.Deals.Add(deal);
        await _db.SaveChangesAsync(ct);
        return deal;
    }

    private async Task<long> EnsureUnknownSupplierAsync(CancellationToken ct)
    {
        const string name = "Aniqlanmagan postavshik";
        var cp = await _db.Counterparties.FirstOrDefaultAsync(c => c.Name == name, ct);
        if (cp is not null) return cp.Id;

        cp = new Counterparty { Name = name, IsSupplier = true };
        _db.Counterparties.Add(cp);
        await _db.SaveChangesAsync(ct);
        return cp.Id;
    }

    private static JsonDocument SafeParse(string raw)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw); }
        catch { return JsonSerializer.SerializeToDocument(new { raw }); }
    }
}

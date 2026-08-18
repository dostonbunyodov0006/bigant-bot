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
    string RawJson);

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
            Direction = DocDirection.In,
            Status = DocStatus.Delivered,
            DocNumber = inc.DocNumber,
            DocDate = inc.DocDate,
            CounterpartyId = sender?.Id,
            Amount = inc.Amount,
            VatAmount = inc.VatAmount,
            DidoxId = inc.DidoxId,
            ParentDocumentId = DealWorkflow.ResolveParent(
                inc.DocType, priorChain),
            Payload = SafeParse(inc.RawJson)
        };
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
            Severity = inc.DocType == DocType.Contract
                ? NotificationSeverity.Warning       // dogovor — e'tibor talab qiladi
                : NotificationSeverity.Info,
            UserId = deal.ManagerId,
            DealId = deal.Id,
            DocumentId = doc.Id,
            Title = $"{who}dan {label} keldi",
            Body = body,
            DedupKey = $"Incoming:doc:{doc.Id}"
        }, ct);

        _audit.Write(null, nameof(Document), doc.Id, "inbox",
            null, new { inc.DidoxId, inc.DocNumber, Type = inc.DocType.ToString() });
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Kiruvchi hujjat ro'yxatga olindi: {Type} №{Number}", inc.DocType, inc.DocNumber);
        return doc;
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
                dovLine = $"Doverennost: №{no ?? "—"}";
            }
            else
            {
                // Doverennostsiz tovar kelgan — bugalter darhol bilsin.
                dovLine = "⚠️ DOVERENNOST YO'Q — tekshiring!";
            }
        }

        var body = $"{who} — №{inc.DocNumber ?? "—"}";
        if (inc.Amount > 0) body += $", {inc.Amount:N0} so'm";
        if (basis is not null) body += $"\nAsos: {basis}";
        if (dovLine is not null) body += $"\n{dovLine}";
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
            List<Deal> candidates;
            if (inc.DocType == DocType.Doverennost)
            {
                candidates = open.Where(d => d.Kind == DealKind.Sale).ToList();
                // Sotuv bitimi topilmasa — xaridga YOPISHTIRMAYMIZ, yangisini ochamiz.
                if (candidates.Count == 0)
                    return await CreateDealAsync(inc, sender.Id, DealKind.Sale, ct);
            }
            else
            {
                candidates = open.Where(d => d.Kind == DealKind.Purchase).ToList();
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

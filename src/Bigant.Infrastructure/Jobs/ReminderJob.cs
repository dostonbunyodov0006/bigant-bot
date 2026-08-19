using Bigant.Domain;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Domain.Workflow;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bigant.Infrastructure.Jobs;

/// <summary>
/// ESLATMA MOTORI — "ko'rmay qolmaslik" kafolati.
/// Har 30 daqiqada ishlaydi. Har bir eslatma DedupKey bilan himoyalangan,
/// shuning uchun bitta muammo bo'yicha kuniga bitta xabar keladi.
/// </summary>
public sealed class ReminderJob
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notify;
    private readonly ILogger<ReminderJob> _log;

    public ReminderJob(AppDbContext db, INotificationService notify, ILogger<ReminderJob> log)
    {
        _db = db; _notify = notify; _log = log;
    }

    /// <summary>Muddat yaqinlashdi deb hisoblanadigan kunlar soni.</summary>
    private const int SoonDays = 3;

    /// <summary>Yirik to'lov chegarasi — undan oshgani alohida ko'riladi.</summary>
    private const decimal LargePaymentLimit = 100_000_000m;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var today = UzTime.Today;
        var stamp = today.ToString("yyyy-MM-dd");
        var raised = 0;

        var deals = await _db.Deals
            .AsNoTracking()
            .Include(d => d.Counterparty)
            .Include(d => d.Steps)
            .Where(d => d.Status == DealStatus.Active)
            .ToListAsync(ct);

        foreach (var d in deals)
        {
            var who = d.Counterparty?.Name ?? "—";

            // ── 1. Pul keldi, tovar berilmadi ── eng shoshilinch
            if (DealWorkflow.IsPaidNotShipped(d))
            {
                raised += await Raise(new Notification
                {
                    Kind = NotificationKind.PaidNotShipped,
                    TargetRole = UserRole.Realizatsiya,
                    Severity = NotificationSeverity.Critical,
                    UserId = d.ManagerId,
                    DealId = d.Id,
                    Title = "\U0001F534 PUL KELDI, TOVAR BERILMADI",
                    Body = $"Kontragent: {who}\n" +
                           $"Shartnoma:  {d.Number}\n\n" +
                           $"Tushgan pul: {d.AmountPaid:N0} so'm\n" +
                           "TTN hali yo'q — tovar berilmagan.\n" +
                           $"Shartnoma ochilganiga {d.AgeDays} kun bo'ldi.",
                    DedupKey = $"PaidNotShipped:deal:{d.Id}:{stamp}"
                }, ct);
            }

            // ── 2. Tovar berildi, pul kelmadi ──
            if (DealWorkflow.IsShippedNotPaid(d))
            {
                var grace = d.Counterparty?.PaymentDays ?? 0;
                var shipped = d.Steps.First(s => s.Step == StepType.Ttn).CompletedAt;
                var daysSince = shipped is null ? 0 : (int)(DateTimeOffset.UtcNow - shipped.Value).TotalDays;

                if (daysSince > grace)
                {
                    raised += await Raise(new Notification
                    {
                        Kind = NotificationKind.ShippedNotPaid,
                        TargetRole = UserRole.Realizatsiya,
                        Severity = daysSince > grace + 7
                            ? NotificationSeverity.Critical
                            : NotificationSeverity.Warning,
                        UserId = d.ManagerId,
                        DealId = d.Id,
                        Title = "\U0001F4B8 TO'LOV KELMADI",
                        Body = $"Kontragent: {who}\n" +
                               $"Shartnoma:  {d.Number}\n\n" +
                               $"Tovar berilgan: {daysSince} kun oldin\n" +
                               $"Otsrochka:      {grace} kun\n" +
                               $"Kechikish:      {daysSince - grace} kun\n\n" +
                               $"\U0001F534 Olinmagan: {d.AmountUnpaid:N0} so'm" +
                               (d.AmountPaid > 0
                                    ? $"\n(qisman to'langan: {d.AmountPaid:N0})" : ""),
                        DedupKey = $"ShippedNotPaid:deal:{d.Id}:{stamp}"
                    }, ct);
                }
            }

            // ── 3. Kutilayotgan qadamlar — HAR BIRIGA ALOHIDA OGOHLANTIRISH ──
            // Egasining talabi: «davernist jonatilmadi», «shot faktura kelmadi» —
            // har bir hujjat uchun o'z xabari bo'lsin va KIM harakat qilishi
            // kerakligi aniq yozilsin.
            //
            // IKKI BOSQICH:
            //   • muddat YAQINLASHGANDA (3 kun qolganda) — ogohlantirish
            //   • muddat O'TIB KETGANDA — qattiqroq signal
            // Faqat o'tgandan keyin aytish kech: hujjat tayyorlashga vaqt kerak.
            foreach (var s in d.Steps.Where(s => s.Status == StepStatus.Pending && s.DueDate != null))
            {
                var due = s.DueDate!.Value;
                var daysLeft = due.DayNumber - today.DayNumber;

                // Muddatga 3 kundan ko'p bo'lsa — jim turamiz.
                if (daysLeft > SoonDays) continue;

                var overdue = daysLeft < 0;
                var late = Math.Abs(daysLeft);
                var we = DealWorkflow.WeAct(d.Kind, s.Step, d.ContractByUs, d.TtnByUs);
                var what = DealWorkflow.StepWaitingText(d.Kind, s.Step, d.ContractByUs, d.TtnByUs);
                // Bitta belgi yetarli: ilgari sarlavhada «⏳ 📥 ...» deb
                // ikkita belgi yonma-yon turardi va ko'z chalg'irdi.
                var icon = overdue ? "⏰" : "⏳";

                var when = overdue
                    ? $"Muddat {late} kun oldin O'TIB KETGAN ({due:dd.MM.yyyy})."
                    : daysLeft == 0
                        ? $"Muddat BUGUN tugaydi ({due:dd.MM.yyyy})."
                        : $"Muddatga {daysLeft} kun qoldi ({due:dd.MM.yyyy}).";

                raised += await Raise(new Notification
                {
                    Kind = NotificationKind.StepOverdue,
                    TargetRole = UserRole.Perehod,
                    Severity = !overdue ? NotificationSeverity.Info
                             : late > 7 ? NotificationSeverity.Critical
                             : NotificationSeverity.Warning,
                    UserId = d.ManagerId,
                    DealId = d.Id,
                    Title = icon + " " + DealWorkflow.StepLabel(s.Step).ToUpperInvariant() +
                            (overdue ? " KECHIKDI" : " MUDDATI YAQIN"),
                    // `what` ichida allaqachon «📤 Biz yuboramiz — hali
                    // yuborilmadi» degan to'liq gap bor, shu sabab kim
                    // harakat qilishi alohida qatorda takrorlanmaydi.
                    Body = $"Kontragent: {who}\n" +
                           $"Shartnoma:  {d.Number}\n\n" +
                           $"{what}\n{when}",
                    // Yaqinlashish va kechikish — ALOHIDA xabar, biri ikkinchisini bosmasin.
                    DedupKey = $"Step{(overdue ? "Late" : "Soon")}:deal:{d.Id}:{s.Step}:{stamp}"
                }, ct);
            }
        }

        raised += await DoverennostExpiryAsync(today, stamp, ct);
        raised += await ChainBreakAsync(stamp, ct);
        raised += await DebtAgingAsync(today, stamp, ct);
        raised += await CounterpartyIncompleteAsync(stamp, ct);
        raised += await OverpaidAsync(stamp, ct);
        raised += await UnlinkedDocumentsAsync(stamp, ct);
        raised += await ChainCompleteAsync(stamp, ct);
        raised += await StaleDealsAsync(today, stamp, ct);

        // ── QO'SHIMCHA NAZORAT ──
        raised += await PaidWithoutInvoiceAsync(today, stamp, ct);
        raised += await BigDebtOnOneAsync(stamp, ct);
        raised += await MonthClosingAsync(today, stamp, ct);
        raised += await AdvancePaymentAsync(stamp, ct);
        raised += await TtnWithoutEsfAsync(today, stamp, ct);
        raised += await EsfWithoutTtnAsync(today, stamp, ct);
        raised += await NoDocumentsTodayAsync(today, stamp, ct);

        // ── OQIM BO'G'INLARI ──
        raised += await AwaitingAcceptanceAsync(today, stamp, ct);
        raised += await NextDocExpectedAsync(today, stamp, ct);

        // ── TO'LIQ QAMROV ──
        raised += await ContractWithoutFollowUpAsync(today, stamp, ct);
        raised += await DebtWithoutContractAsync(stamp, ct);
        raised += await ContractDueSoonAsync(today, stamp, ct);
        raised += await QuietCounterpartyAsync(today, stamp, ct);
        raised += await PaymentBeforeContractAsync(stamp, ct);
        raised += await DuplicatePaymentSameDayAsync(stamp, ct);
        raised += await LargePaymentAsync(stamp, ct);
        raised += await ClientManyOverdueAsync(today, stamp, ct);
        raised += await VatRateChangedAsync(stamp, ct);
        raised += await TurnoverDropAsync(today, stamp, ct);
        raised += await UnreadPileUpAsync(stamp, ct);
        raised += await StaleUnlinkedDocsAsync(stamp, ct);

        if (raised > 0) _log.LogInformation("Eslatma motori: {Count} ta yangi eslatma", raised);
        await _notify.PushPendingAsync(ct);
    }

    // ══════════════════════════════════════════════════════════════
    //  REALIZATSIYA BUXGALTERI UCHUN
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// QARZ QARIB QOLDI. 30 / 60 / 90 kun — uchta bosqich.
    /// «Qancha vaqtdan beri qarzdor» degan savolga javob. Har bosqichda
    /// bitta xabar: 30 kunlik xabar 60 kunlikni bosib ketmaydi.
    /// </summary>
    private async Task<int> DebtAgingAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        int[] steps = [30, 60, 90];
        var raised = 0;

        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active && d.AmountPaid < d.AmountTotal)
            .Select(d => new
            {
                d.Id, d.Number, d.Kind, d.ManagerId,
                Who = d.Counterparty!.Name,
                Debt = d.AmountTotal - d.AmountPaid,
                d.AmountTotal, d.AmountPaid,
                Since = d.ContractDate ?? DateOnly.FromDateTime(d.CreatedAt.UtcDateTime),
            })
            .ToListAsync(ct);

        foreach (var r in rows)
        {
            var age = today.DayNumber - r.Since.DayNumber;
            var step = steps.LastOrDefault(x => age >= x);
            if (step == 0) continue;

            var buy = r.Kind == DealKind.Purchase;
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.DebtAging,
                TargetRole = UserRole.Realizatsiya,
                Severity = step >= 90 ? NotificationSeverity.Critical
                         : step >= 60 ? NotificationSeverity.Warning
                         : NotificationSeverity.Info,
                UserId = r.ManagerId,
                DealId = r.Id,
                Title = $"\U0001F4C6 QARZ {step} KUNDAN OSHDI",
                Body = $"Kontragent: {r.Who}\n" +
                       $"Shartnoma:  {r.Number}\n\n" +
                       $"Qarz yoshi: {age} kun\n" +
                       $"Shartnoma:  {r.AmountTotal:N0}\n" +
                       (buy ? $"O'tkazdik:  {r.AmountPaid:N0}\n" : $"Tushdi:     {r.AmountPaid:N0}\n") +
                       (buy ? $"\U0001F534 Qoldi to'lash: {r.Debt:N0}"
                            : $"\U0001F7E2 Qoldi olish:   {r.Debt:N0}"),
                DedupKey = $"DebtAging:deal:{r.Id}:{step}"
            }, ct);
        }

        return raised;
    }

    /// <summary>
    /// KONTRAGENTNI TANIB BO'LMAYAPTI.
    ///
    /// Didox'dan hujjat kelganda kontragent INN bo'yicha avtomatik ochiladi.
    /// INN bo'lmasa esa keyingi hujjatlarni AYNAN shu kontragentga bog'lay
    /// olmaymiz — bir xil nomli ikki yozuv paydo bo'ladi va qarz ikkiga
    /// bo'linib ketadi. Faqat shu holat ogohlantiriladi.
    ///
    /// Direktor F.I.Sh, hisob raqami va manzil endi so'ralmaydi: hujjatni
    /// Didox yaratadi, ular Didox tomonida turadi.
    /// </summary>
    private async Task<int> CounterpartyIncompleteAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;
        var rows = await _db.Counterparties.AsNoTracking()
            .Where(c => !c.IsBlocked)
            .ToListAsync(ct);

        foreach (var c in rows)
        {
            var missing = c.MissingForDidox().ToList();
            if (missing.Count == 0) continue;

            raised += await Raise(new Notification
            {
                Kind = NotificationKind.CounterpartyIncomplete,
                TargetRole = UserRole.Realizatsiya,
                Severity = NotificationSeverity.Warning,
                Title = "\U0001F4CB KONTRAGENTNI ANIQLAB BO'LMAYDI",
                Body = $"Kontragent: {c.Name}\n\n" +
                       "Yetishmayapti:\n" + string.Join("\n", missing.Select(m => "• " + m)) + "\n\n" +
                       "INN siz keyingi hujjatlar shu kontragentga bog'lanmaydi —\n" +
                       "bir xil nomli ikkinchi yozuv paydo bo'lib, qarz ikkiga bo'linadi.",
                DedupKey = $"CpIncomplete:{c.Id}:{stamp}"
            }, ct);
        }
        return raised;
    }

    /// <summary>TO'LOV DOGOVORDAN OLDIN — sana tartibi buzilgan.</summary>
    private async Task<int> PaymentBeforeContractAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;
        var rows = await _db.Payments.AsNoTracking()
            .Where(p => p.Deal != null && p.Deal.ContractDate != null && p.PayDate < p.Deal.ContractDate)
            .Select(p => new
            {
                p.Id, p.Amount, p.PayDate,
                Number = p.Deal!.Number, p.Deal.ContractDate, p.Deal.ManagerId, p.DealId,
                Who = p.Deal.Counterparty!.Name,
            })
            .Take(50)
            .ToListAsync(ct);

        foreach (var p in rows)
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.PaymentBeforeContract,
                TargetRole = UserRole.Realizatsiya,
                Severity = NotificationSeverity.Warning,
                UserId = p.ManagerId, DealId = p.DealId,
                Title = "📅 TO'LOV DOGOVORDAN OLDIN",
                Body = $"Kontragent: {p.Who}\n" +
                       $"Shartnoma:  {p.Number}\n\n" +
                       $"To'lov sanasi:   {p.PayDate:dd.MM.yyyy}\n" +
                       $"Dogovor sanasi:  {p.ContractDate:dd.MM.yyyy}\n" +
                       $"Summa: {p.Amount:N0} so'm\n\n" +
                       "Avans bo'lishi mumkin, yoki sana xato kiritilgan.",
                DedupKey = $"PayBefore:pay:{p.Id}"
            }, ct);
        return raised;
    }

    /// <summary>BIR KUNDA BIR XIL SUMMA IKKI MARTA — ikki marta kiritilgan bo'lishi mumkin.</summary>
    private async Task<int> DuplicatePaymentSameDayAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;
        var rows = await _db.Payments.AsNoTracking()
            .GroupBy(p => new { p.CounterpartyId, p.PayDate, p.Amount })
            .Where(g => g.Count() > 1)
            .Select(g => new { g.Key.CounterpartyId, g.Key.PayDate, g.Key.Amount, Count = g.Count() })
            .Take(30)
            .ToListAsync(ct);

        foreach (var r in rows)
        {
            var who = await _db.Counterparties.AsNoTracking()
                .Where(c => c.Id == r.CounterpartyId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";

            raised += await Raise(new Notification
            {
                Kind = NotificationKind.DuplicatePaymentSameDay,
                TargetRole = UserRole.Realizatsiya,
                Severity = NotificationSeverity.Warning,
                Title = "🔁 BIR KUNDA BIR XIL TO'LOV",
                Body = $"Kontragent: {who}\n\n" +
                       $"Sana:  {r.PayDate:dd.MM.yyyy}\n" +
                       $"Summa: {r.Amount:N0} so'm\n" +
                       $"Marta: {r.Count} ta\n\n" +
                       "Haqiqatan ikki marta to'langanmi yoki xato kiritilganmi — tekshiring.",
                DedupKey = $"DupPay:{r.CounterpartyId}:{r.PayDate:yyyyMMdd}:{r.Amount}"
            }, ct);
        }
        return raised;
    }

    /// <summary>YIRIK TO'LOV — nazorat uchun alohida ko'rsatiladi.</summary>
    private async Task<int> LargePaymentAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;
        var rows = await _db.Payments.AsNoTracking()
            .Where(p => p.Amount >= LargePaymentLimit)
            .OrderByDescending(p => p.Id)
            .Select(p => new
            {
                p.Id, p.Amount, p.PayDate, p.Direction, p.DealId,
                Number = p.Deal!.Number, Who = p.Deal.Counterparty!.Name,
            })
            .Take(20)
            .ToListAsync(ct);

        foreach (var p in rows)
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.LargePayment,
                TargetRole = UserRole.Realizatsiya,
                Severity = NotificationSeverity.Info,
                DealId = p.DealId,
                Title = "💵 YIRIK TO'LOV",
                Body = $"Kontragent: {p.Who}\n" +
                       $"Shartnoma:  {p.Number}\n\n" +
                       (p.Direction == PaymentDirection.Out ? "Biz o'tkazdik: " : "Bizga tushdi:  ") +
                       $"{p.Amount:N0} so'm\n" +
                       $"Sana: {p.PayDate:dd.MM.yyyy}\n\n" +
                       $"Chegara {LargePaymentLimit:N0} so'm — undan oshdi.",
                DedupKey = $"BigPay:pay:{p.Id}"
            }, ct);
        return raised;
    }

    /// <summary>BITTA KLIENTDA BIR NECHTA KECHIKKAN TO'LOV.</summary>
    private async Task<int> ClientManyOverdueAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        const int MinCount = 3;
        var raised = 0;

        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active
                     && d.Kind == DealKind.Sale
                     && d.AmountTotal > d.AmountPaid
                     && d.Steps.Any(x => x.Step == StepType.Payment
                                      && x.Status == StepStatus.Pending
                                      && x.DueDate != null && x.DueDate < today))
            .GroupBy(d => new { d.CounterpartyId, Who = d.Counterparty!.Name })
            .Select(g => new
            {
                g.Key.CounterpartyId, g.Key.Who,
                Count = g.Count(),
                Debt = g.Sum(x => x.AmountTotal - x.AmountPaid),
            })
            .Where(x => x.Count >= MinCount)
            .ToListAsync(ct);

        foreach (var r in rows)
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.ClientManyOverdue,
                TargetRole = UserRole.Realizatsiya,
                Severity = NotificationSeverity.Critical,
                Title = "🚨 KLIENTDA BIR NECHTA KECHIKKAN TO'LOV",
                Body = $"Kontragent: {r.Who}\n\n" +
                       $"Kechikkan shartnoma: {r.Count} ta\n" +
                       $"Jami qarzi: {r.Debt:N0} so'm\n\n" +
                       "Yangi sotuvni to'xtatib turishni ko'rib chiqing.",
                DedupKey = $"ManyOverdue:cp:{r.CounterpartyId}:{stamp}"
            }, ct);
        return raised;
    }

    /// <summary>QQS STAVKASI HUJJATLAR ORASIDA O'ZGARGAN.</summary>
    private async Task<int> VatRateChangedAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;
        var rows = await _db.Documents.AsNoTracking()
            .Where(d => d.Amount > 0 && d.VatAmount > 0 && d.Deal!.Status == DealStatus.Active)
            .Select(d => new { d.Id, d.DealId, d.DocType, d.DocNumber, d.Amount, d.VatAmount,
                               Number = d.Deal!.Number, Who = d.Deal.Counterparty!.Name })
            .ToListAsync(ct);

        foreach (var g in rows.GroupBy(x => x.DealId))
        {
            var rates = g.Select(x => Math.Round(x.VatAmount / x.Amount, 3)).Distinct().ToList();
            if (rates.Count < 2) continue;

            var first = g.First();
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.VatRateChanged,
                TargetRole = UserRole.Realizatsiya,
                Severity = NotificationSeverity.Warning,
                DealId = g.Key,
                Title = "🧮 QQS STAVKASI MOS KELMAYAPTI",
                Body = $"Kontragent: {first.Who}\n" +
                       $"Shartnoma:  {first.Number}\n\n" +
                       "Shu shartnoma hujjatlarida QQS nisbati har xil:\n" +
                       string.Join("\n", g.Select(x =>
                           $"• {DealWorkflow.DocLabel(x.DocType)} №{x.DocNumber ?? "—"}: " +
                           $"{Math.Round(x.VatAmount / x.Amount * 100, 1)}%")) + "\n\n" +
                       "Hujjatlardan birida xato bo'lishi mumkin.",
                DedupKey = $"VatRate:deal:{g.Key}:{stamp}"
            }, ct);
        }
        return raised;
    }

    /// <summary>OYLIK AYLANMA KESKIN TUSHDI.</summary>
    private async Task<int> TurnoverDropAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        // Oyning o'rtasidan keyin taqqoslash ma'noga ega bo'ladi.
        if (today.Day < 15) return 0;

        var thisFrom = new DateOnly(today.Year, today.Month, 1);
        var prevFrom = thisFrom.AddMonths(-1);

        var now = await _db.Documents.AsNoTracking()
            .Where(d => d.DocDate >= thisFrom && d.DocDate <= today).SumAsync(d => (decimal?)d.Amount, ct) ?? 0;
        var prev = await _db.Documents.AsNoTracking()
            .Where(d => d.DocDate >= prevFrom && d.DocDate < thisFrom).SumAsync(d => (decimal?)d.Amount, ct) ?? 0;

        if (prev <= 0 || now >= prev * 0.5m) return 0;

        return await Raise(new Notification
        {
            Kind = NotificationKind.TurnoverDrop,
            TargetRole = UserRole.Realizatsiya,
            Severity = NotificationSeverity.Warning,
            Title = "📉 OYLIK AYLANMA TUSHDI",
            Body = $"O'tgan oy: {prev:N0} so'm\n" +
                   $"Shu oy:    {now:N0} so'm\n\n" +
                   $"Farq: {(now - prev) / prev * 100:N0}%\n\n" +
                   "Ish kamayganmi yoki hujjatlar Didox'ga tushmayaptimi — aniqlang.",
            DedupKey = $"Turnover:{today:yyyy-MM}"
        }, ct);
    }

    /// <summary>O'QILMAGAN XABARLAR TO'PLANIB QOLDI.</summary>
    private async Task<int> UnreadPileUpAsync(string stamp, CancellationToken ct)
    {
        const int Limit = 200;
        var unread = await _db.Notifications.CountAsync(n => !n.IsRead, ct);
        if (unread < Limit) return 0;

        return await Raise(new Notification
        {
            Kind = NotificationKind.UnreadPileUp,
            TargetRole = UserRole.Admin,
            Severity = NotificationSeverity.Warning,
            Title = "📬 O'QILMAGAN XABARLAR TO'PLANDI",
            Body = $"O'qilmagan xabar: {unread} ta\n\n" +
                   "Bugalter xabarlarni ko'rmayapti yoki ular haqiqatan ko'payib ketgan.\n" +
                   "Dashboardda «Hammasini o'qildi» tugmasi bor.",
            DedupKey = $"Unread:{stamp}"
        }, ct);
    }

    /// <summary>BOG'LANMAGAN HUJJATLAR UZOQ TURIBDI.</summary>
    private async Task<int> StaleUnlinkedDocsAsync(string stamp, CancellationToken ct)
    {
        const int Days = 7;
        var edge = DateTimeOffset.UtcNow.AddDays(-Days);

        var count = await _db.Documents.CountAsync(
            d => d.DealId == null && d.Status != DocStatus.Cancelled && d.CreatedAt < edge, ct);
        if (count == 0) return 0;

        return await Raise(new Notification
        {
            Kind = NotificationKind.StaleUnlinkedDocs,
            TargetRole = UserRole.Admin,
            Severity = NotificationSeverity.Warning,
            Title = "📎 BOG'LANMAGAN HUJJATLAR UZOQ TURIBDI",
            Body = $"{count} ta hujjat {Days} kundan beri hech qaysi shartnomaga bog'lanmagan.\n\n" +
                   "Ular hisobotga kirmaydi va qarzda ko'rinmaydi.",
            DedupKey = $"StaleUnlinked:{stamp}"
        }, ct);
    }


    /// <summary>
    /// DOVERENNOST MUDDATI. Tovar hali olinmagan bo'lsa — muddat tugashi
    /// hamma ishni bekor qiladi: yangi doverennost chiqarish kerak bo'ladi.
    /// </summary>
    private async Task<int> DoverennostExpiryAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        var soon = today.AddDays(SoonDays);
        var raised = 0;

        var rows = await _db.Doverennosti.AsNoTracking()
            .Include(d => d.Document).ThenInclude(x => x!.Deal).ThenInclude(x => x!.Counterparty)
            .Where(d => d.ValidUntil <= soon
                     && d.Document.Status != DocStatus.Cancelled
                     && d.Document.Deal!.Status == DealStatus.Active
                     // Tovar allaqachon kelgan bo'lsa ishonchnoma kerak emas.
                     && d.Document.Deal.Steps.Any(s => s.Step == StepType.Ttn
                                                    && s.Status == StepStatus.Pending))
            .ToListAsync(ct);

        foreach (var d in rows)
        {
            var deal = d.Document.Deal!;
            var who = deal.Counterparty?.Name ?? "—";
            var expired = d.ValidUntil < today;
            var days = Math.Abs(today.DayNumber - d.ValidUntil.DayNumber);

            raised += await Raise(new Notification
            {
                Kind = expired ? NotificationKind.DoverennostExpired
                               : NotificationKind.DoverennostExpiringSoon,
                TargetRole = UserRole.Perehod,
                Severity = expired ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                UserId = deal.ManagerId,
                DealId = deal.Id,
                DocumentId = d.DocumentId,
                Title = expired ? "⛔ DOVERENNOST MUDDATI O'TDI" : "⏳ DOVERENNOST MUDDATI TUGAYAPTI",
                Body = $"Kontragent: {who}\n" +
                       $"Shartnoma:  {deal.Number}\n" +
                       $"Ishonchli shaxs: {d.EmployeeName}\n\n" +
                       (expired
                            ? $"Muddat {days} kun oldin tugagan ({d.ValidUntil:dd.MM.yyyy}).\n" +
                              "Tovar hali olinmagan — YANGI DOVERENNOST KERAK."
                            : $"Muddat tugashiga {days} kun qoldi ({d.ValidUntil:dd.MM.yyyy}).\n" +
                              "Tovar hali olinmagan."),
                DedupKey = $"DovExpiry:doc:{d.DocumentId}:{stamp}"
            }, ct);
        }
        return raised;
    }

    /// <summary>
    /// HUJJATLAR TARTIBI BUZILGAN.
    /// «Aynan o'sha dogovorga doverennost, o'shanga faktura» qoidasi
    /// buzilgan shartnomalar — zanjir bo'g'ini yetishmayapti yoki noto'g'ri.
    /// </summary>
    private async Task<int> ChainBreakAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;

        var deals = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active)
            .Select(d => new
            {
                d.Id, d.Number, d.ManagerId,
                Who = d.Counterparty!.Name,
                Docs = d.Documents
                    .Where(x => x.Status != DocStatus.Cancelled && x.Status != DocStatus.Rejected)
                    .Select(x => new { x.DocType, x.Id, x.ParentDocumentId })
                    .ToList(),
            })
            .ToListAsync(ct);

        foreach (var d in deals)
        {
            if (d.Docs.Count < 2) continue;

            var breaks = DealWorkflow.CheckChain(
                d.Docs.Select(x => (x.DocType, x.Id, x.ParentDocumentId)));
            if (breaks.Count == 0) continue;

            raised += await Raise(new Notification
            {
                Kind = NotificationKind.ChainOutOfOrder,
                TargetRole = UserRole.Perehod,
                Severity = NotificationSeverity.Warning,
                UserId = d.ManagerId,
                DealId = d.Id,
                Title = "⚠️ HUJJATLAR TARTIBI BUZILGAN",
                Body = $"Kontragent: {d.Who}\n" +
                       $"Shartnoma:  {d.Number}\n\n" +
                       string.Join("\n", breaks.Select(b =>
                           $"• {DealWorkflow.DocLabel(b.Doc)}: {b.Problem}")) + "\n\n" +
                       "To'g'ri tartib: dogovor → doverennost → TTN → faktura.",
                DedupKey = $"ChainBreak:deal:{d.Id}:{stamp}"
            }, ct);
        }
        return raised;
    }

    /// <summary>
    /// ORTIQCHA TO'LOV. Shartnoma summasidan ko'p to'langan —
    /// yo xato kiritilgan, yo avans. Ikkalasi ham tekshirishga arziydi.
    /// </summary>
    private async Task<int> OverpaidAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;
        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active
                     && d.AmountTotal > 0
                     && d.AmountPaid > d.AmountTotal)
            .Select(d => new { d.Id, d.Number, d.ManagerId, d.AmountTotal, d.AmountPaid, Who = d.Counterparty!.Name })
            .ToListAsync(ct);

        foreach (var d in rows)
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.Overpaid,
                TargetRole = UserRole.Realizatsiya,
                Severity = NotificationSeverity.Warning,
                UserId = d.ManagerId,
                DealId = d.Id,
                Title = "\U0001F4B0 ORTIQCHA TO'LOV",
                Body = $"Kontragent: {d.Who}\n" +
                       $"Shartnoma:  {d.Number}\n\n" +
                       $"Shartnoma summasi: {d.AmountTotal:N0} so'm\n" +
                       $"To'langan:         {d.AmountPaid:N0} so'm\n" +
                       $"⚠️ Ortiqcha:        {d.AmountPaid - d.AmountTotal:N0} so'm\n\n" +
                       "Xato kiritilgan yoki avans bo'lishi mumkin — tekshiring.",
                DedupKey = $"Overpaid:deal:{d.Id}:{d.AmountPaid}"
            }, ct);
        return raised;
    }

    /// <summary>
    /// SHARTNOMASI ANIQLANMAGAN HUJJAT.
    /// Didox'dan kelgan, lekin hech qaysi shartnomaga bog'lanmagan hujjat —
    /// u hisobotga ham, qarzga ham kirmaydi.
    /// </summary>
    private async Task<int> UnlinkedDocumentsAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;
        var rows = await _db.Documents.AsNoTracking()
            .Where(d => d.DealId == null && d.Status != DocStatus.Cancelled)
            .Select(d => new { d.Id, d.DocType, d.DocNumber, d.DocDate, d.Amount,
                               Who = d.Counterparty != null ? d.Counterparty.Name : null })
            .Take(30)
            .ToListAsync(ct);

        foreach (var d in rows)
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.UnlinkedDocument,
                TargetRole = UserRole.Perehod,
                Severity = NotificationSeverity.Warning,
                DocumentId = d.Id,
                Title = "\U0001F4CE SHARTNOMASI ANIQLANMAGAN HUJJAT",
                Body = $"Kontragent: {d.Who ?? "noma'lum"}\n\n" +
                       $"{DealWorkflow.DocLabel(d.DocType)} №{d.DocNumber ?? "—"}\n" +
                       $"Sana:  {d.DocDate:dd.MM.yyyy}\n" +
                       $"Summa: {d.Amount:N0} so'm\n\n" +
                       "Bu hujjat hech qaysi shartnomaga bog'lanmagan —\n" +
                       "hisobotga va qarzga kirmaydi.",
                DedupKey = $"Unlinked:doc:{d.Id}:{stamp}"
            }, ct);
        return raised;
    }

    /// <summary>
    /// HAMMA HUJJAT YIG'ILDI — shartnomani yopsa bo'ladi.
    /// </summary>
    private async Task<int> ChainCompleteAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;

        var deals = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active)
            .Select(d => new
            {
                DealId = d.Id, d.Number, d.ManagerId,
                Who = d.Counterparty!.Name,
                Types = d.Documents
                    .Where(x => x.Status != DocStatus.Cancelled && x.Status != DocStatus.Rejected)
                    .Select(x => x.DocType).ToList(),
            })
            .ToListAsync(ct);

        foreach (var f in deals)
        {
            var types = f.Types.Distinct().ToList();
            // Dogovor + doverennost + TTN + faktura — to'rttasi ham bo'lsa to'liq.
            var need = new[] { DocType.Contract, DocType.Doverennost, DocType.Ttn, DocType.Esf };
            if (!need.All(types.Contains)) continue;

            var mine = types;
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.ChainComplete,
                TargetRole = UserRole.Perehod,
                Severity = NotificationSeverity.Info,
                UserId = f.ManagerId,
                DealId = f.DealId,
                // «Zanjir to'liq» dasturchi tili edi. Bugalter uchun muhimi:
                // hamma hujjat yig'ildi va shartnomani yopish mumkin.
                Title = "✅ HAMMA HUJJAT YIG'ILDI",
                Body = $"Kontragent: {f.Who}\n" +
                       $"Shartnoma:  {f.Number}\n\n" +
                       $"{mine.Count} ta hujjat bir-biriga to'g'ri bog'langan:\n" +
                       string.Join(" → ", DealWorkflow.Chain
                            .Where(t => types.Contains(t))
                            .Select(DealWorkflow.DocLabel)) + "\n\n" +
                       "Yetishmayotgan hujjat yo'q — shartnomani yopsa bo'ladi.",
                DedupKey = $"ChainComplete:deal:{f.DealId}"
            }, ct);
        }
        return raised;
    }

    /// <summary>
    /// SHARTNOMA UZOQ YOPILMAYAPTI. Ochilganiga ko'p vaqt bo'ldi,
    /// hali ham harakat yo'q — unutilgan bo'lishi mumkin.
    /// </summary>
    private async Task<int> StaleDealsAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        const int StaleDays = 60;
        var edge = DateTimeOffset.UtcNow.AddDays(-StaleDays);
        var raised = 0;

        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active && d.CreatedAt < edge)
            .Select(d => new { d.Id, d.Number, d.ManagerId, d.CreatedAt, d.AmountTotal, d.AmountPaid,
                               Who = d.Counterparty!.Name })
            .Take(50)
            .ToListAsync(ct);

        foreach (var d in rows)
        {
            var days = (int)(DateTimeOffset.UtcNow - d.CreatedAt).TotalDays;
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.DealStale,
                TargetRole = UserRole.Perehod,
                Severity = NotificationSeverity.Warning,
                UserId = d.ManagerId,
                DealId = d.Id,
                Title = "\U0001F55B SHARTNOMA UZOQ YOPILMAYAPTI",
                Body = $"Kontragent: {d.Who}\n" +
                       $"Shartnoma:  {d.Number}\n\n" +
                       $"Ochilganiga {days} kun bo'ldi.\n" +
                       $"Yopilmagan qarz: {d.AmountTotal - d.AmountPaid:N0} so'm\n\n" +
                       "Ish tugaganmi yoki unutilganmi — aniqlang.",
                DedupKey = $"Stale:deal:{d.Id}:{stamp}"
            }, ct);
        }
        return raised;
    }

    // ══════════════════════════════════════════════════════════════
    //  QO'SHIMCHA NAZORAT (2026-08-19)
    //
    //  Hammasi Didox hujjatining o'zidagi maydonlarga tayanadi. Har biri
    //  ALOHIDA xabar chiqaradi va aniq bitta rolga boradi — bugalter
    //  o'ziga tegishli bo'lmagan narsani o'qib vaqt sarflamasin.
    // ══════════════════════════════════════════════════════════════

    /// <summary>Bitta kontragentdagi qarz shu chegaradan oshsa ogohlantiramiz.</summary>
    private const decimal BigDebtLimit = 50_000_000m;

    /// <summary>
    /// PUL O'TDI, FAKTURA YO'Q.
    ///
    /// Nega muhim: faktura bo'lmasa QQS ni hisobga ololmaymiz — pul to'langan,
    /// lekin soliq chegirmasi yo'q.
    /// </summary>
    private async Task<int> PaidWithoutInvoiceAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        const int WaitDays = 5;
        var raised = 0;

        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active
                     && d.Kind == DealKind.Purchase
                     && d.AmountPaid > 0
                     && !d.Documents.Any(x => x.DocType == DocType.Esf
                                           && x.Status != DocStatus.Cancelled))
            .Select(d => new
            {
                d.Id, d.Number, d.ManagerId, d.AmountPaid,
                Who = d.Counterparty!.Name,
                LastPay = d.Payments.Max(p => (DateOnly?)p.PayDate)
            })
            .ToListAsync(ct);

        foreach (var d in rows)
        {
            if (d.LastPay is not { } pay) continue;
            var days = today.DayNumber - pay.DayNumber;
            if (days < WaitDays) continue;

            raised += await Raise(new Notification
            {
                Kind = NotificationKind.PaidWithoutInvoice,
                TargetRole = UserRole.Realizatsiya,
                Severity = days > 30 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                UserId = d.ManagerId,
                DealId = d.Id,
                Title = "🧾 PUL O'TDI, FAKTURA YO'Q",
                Body = $"Kontragent: {d.Who}\n" +
                       $"Shartnoma:  {d.Number}\n\n" +
                       $"To'langan:     {d.AmountPaid:N0} so'm\n" +
                       $"Oxirgi to'lov: {pay:dd.MM.yyyy} ({days} kun oldin)\n\n" +
                       "Faktura kelmaguncha QQS ni hisobga ola olmaymiz.\n" +
                       "Kontragentdan fakturani so'rang.",
                DedupKey = $"PaidNoEsf:deal:{d.Id}:{stamp}"
            }, ct);
        }
        return raised;
    }

    /// <summary>
    /// BITTA KONTRAGENTDA KATTA QARZ.
    ///
    /// Alohida shartnomalar kichik ko'rinadi, lekin yig'ilib katta summa
    /// bo'ladi. Bir joyga qancha pul bog'lanib qolganini ko'rsatadi.
    /// </summary>
    private async Task<int> BigDebtOnOneAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;

        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active && d.AmountTotal > d.AmountPaid)
            .GroupBy(d => new { d.CounterpartyId, d.Kind, Who = d.Counterparty!.Name })
            .Select(g => new
            {
                g.Key.CounterpartyId, g.Key.Kind, g.Key.Who,
                Debt = g.Sum(x => x.AmountTotal - x.AmountPaid),
                Count = g.Count()
            })
            .Where(x => x.Debt >= BigDebtLimit)
            .ToListAsync(ct);

        foreach (var r in rows)
        {
            var we = r.Kind == DealKind.Purchase;
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.BigDebtOnOne,
                TargetRole = UserRole.Realizatsiya,
                Severity = NotificationSeverity.Warning,
                Title = we ? "🔴 BITTA YERGA KATTA QARZIMIZ" : "🟢 BITTA JOYDA KATTA QARZDORLIK",
                Body = $"Kontragent: {r.Who}\n\n" +
                       (we ? $"Biz qarzdormiz: {r.Debt:N0} so'm\n"
                           : $"Bizga qarzdor:  {r.Debt:N0} so'm\n") +
                       $"Ochiq shartnoma: {r.Count} ta\n\n" +
                       $"Chegara {BigDebtLimit:N0} so'm — undan oshdi.",
                DedupKey = $"BigDebt:cp:{r.CounterpartyId}:{r.Kind}:{stamp}"
            }, ct);
        }
        return raised;
    }

    /// <summary>
    /// OY YOPILAYAPTI. Oxirgi uch kunda yopilmagan bitimlar haqida.
    /// Oy yopilgandan keyin kelgan hujjat hisobotni qayta ochishga majbur qiladi.
    /// </summary>
    private async Task<int> MonthClosingAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        var lastDay = DateTime.DaysInMonth(today.Year, today.Month);
        if (lastDay - today.Day > 2) return 0;      // faqat oxirgi 3 kun

        var open = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active && d.AmountTotal > d.AmountPaid)
            .Select(d => new { d.AmountTotal, d.AmountPaid, d.Kind })
            .ToListAsync(ct);

        if (open.Count == 0) return 0;

        var weOwe = open.Where(x => x.Kind == DealKind.Purchase).Sum(x => x.AmountTotal - x.AmountPaid);
        var owedUs = open.Where(x => x.Kind == DealKind.Sale).Sum(x => x.AmountTotal - x.AmountPaid);

        return await Raise(new Notification
        {
            Kind = NotificationKind.MonthClosing,
            TargetRole = UserRole.Realizatsiya,
            Severity = NotificationSeverity.Warning,
            Title = "📆 OY YOPILAYAPTI",
            Body = $"Oy tugashiga {lastDay - today.Day} kun qoldi.\n\n" +
                   $"Yopilmagan bitim: {open.Count} ta\n" +
                   $"Biz qarzdormiz:   {weOwe:N0} so'm\n" +
                   $"Bizga qarzdor:    {owedUs:N0} so'm\n\n" +
                   "Yetishmayotgan hujjatlarni oy ichida yig'ib oling.",
            DedupKey = $"MonthClose:{today:yyyy-MM}"
        }, ct);
    }

    /// <summary>
    /// AVANS — hujjatsiz to'lov.
    /// Pul o'tkazilgan, lekin bitimda birorta ham hujjat yo'q. Avans bo'lishi
    /// mumkin, adashib boshqa kontragentga o'tkazilgan ham bo'lishi mumkin.
    /// </summary>
    private async Task<int> AdvancePaymentAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;

        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active
                     && d.AmountPaid > 0
                     && !d.Documents.Any(x => x.Status != DocStatus.Cancelled))
            .Select(d => new { d.Id, d.Number, d.ManagerId, d.AmountPaid, Who = d.Counterparty!.Name })
            .ToListAsync(ct);

        foreach (var d in rows)
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.AdvancePayment,
                TargetRole = UserRole.Realizatsiya,
                Severity = NotificationSeverity.Warning,
                UserId = d.ManagerId,
                DealId = d.Id,
                Title = "💳 HUJJATSIZ TO'LOV",
                Body = $"Kontragent: {d.Who}\n" +
                       $"Shartnoma:  {d.Number}\n\n" +
                       $"To'langan: {d.AmountPaid:N0} so'm\n" +
                       "Bu bitimda birorta hujjat yo'q.\n\n" +
                       "Avans bo'lsa — normal. Bo'lmasa, pul boshqa yerga ketgan bo'lishi mumkin.",
                DedupKey = $"Advance:deal:{d.Id}:{stamp}"
            }, ct);

        return raised;
    }

    /// <summary>
    /// TOVAR KELDI, FAKTURA KELMADI. TTN bor, ESF yo'q.
    /// Tovar omborda, lekin rasman xarid qilinmagan — QQS ham hisobga olinmaydi.
    /// </summary>
    private async Task<int> TtnWithoutEsfAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        const int WaitDays = 3;
        var raised = 0;

        var rows = await _db.Documents.AsNoTracking()
            .Where(d => d.DocType == DocType.Ttn
                     && d.Status != DocStatus.Cancelled
                     && d.Deal!.Status == DealStatus.Active
                     && !d.Deal.Documents.Any(x => x.DocType == DocType.Esf
                                                && x.Status != DocStatus.Cancelled))
            .Select(d => new
            {
                d.Id, d.DocNumber, d.DocDate, d.DealId,
                Number = d.Deal!.Number, d.Deal.ManagerId,
                Who = d.Deal.Counterparty!.Name
            })
            .ToListAsync(ct);

        foreach (var d in rows)
        {
            var days = today.DayNumber - d.DocDate.DayNumber;
            if (days < WaitDays) continue;

            raised += await Raise(new Notification
            {
                Kind = NotificationKind.TtnWithoutEsf,
                TargetRole = UserRole.Perehod,
                Severity = days > 14 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                UserId = d.ManagerId,
                DealId = d.DealId,
                DocumentId = d.Id,
                Title = "🚚 TOVAR KELDI, FAKTURA YO'Q",
                Body = $"Kontragent: {d.Who}\n" +
                       $"Shartnoma:  {d.Number}\n\n" +
                       $"TTN №{d.DocNumber ?? "—"} — {d.DocDate:dd.MM.yyyy} ({days} kun oldin)\n" +
                       "Schyot-faktura hali kelmadi.\n\n" +
                       "Tovar omborda, lekin rasman xarid qilinmagan.",
                DedupKey = $"TtnNoEsf:doc:{d.Id}:{stamp}"
            }, ct);
        }
        return raised;
    }

    /// <summary>
    /// FAKTURA KELDI, TOVAR KELMADI. ESF bor, TTN yo'q.
    /// Rasman xarid qilingan, lekin tovar yo'q — yo yo'lda, yo umuman kelmagan.
    /// </summary>
    private async Task<int> EsfWithoutTtnAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        const int WaitDays = 3;
        var raised = 0;

        var rows = await _db.Documents.AsNoTracking()
            .Where(d => d.DocType == DocType.Esf
                     && d.Status != DocStatus.Cancelled
                     && d.Deal!.Status == DealStatus.Active
                     && d.Deal.Kind == DealKind.Purchase
                     && !d.Deal.Documents.Any(x => x.DocType == DocType.Ttn
                                                && x.Status != DocStatus.Cancelled))
            .Select(d => new
            {
                d.Id, d.DocNumber, d.DocDate, d.Amount, d.DealId,
                Number = d.Deal!.Number, d.Deal.ManagerId,
                Who = d.Deal.Counterparty!.Name
            })
            .ToListAsync(ct);

        foreach (var d in rows)
        {
            var days = today.DayNumber - d.DocDate.DayNumber;
            if (days < WaitDays) continue;

            raised += await Raise(new Notification
            {
                Kind = NotificationKind.EsfWithoutTtn,
                TargetRole = UserRole.Perehod,
                Severity = days > 14 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                UserId = d.ManagerId,
                DealId = d.DealId,
                DocumentId = d.Id,
                Title = "📦 FAKTURA BOR, TOVAR YO'Q",
                Body = $"Kontragent: {d.Who}\n" +
                       $"Shartnoma:  {d.Number}\n\n" +
                       $"Faktura №{d.DocNumber ?? "—"} — {d.DocDate:dd.MM.yyyy} ({days} kun oldin)\n" +
                       $"Summa: {d.Amount:N0} so'm\n" +
                       "TTN hali kelmadi.\n\n" +
                       "Tovar yo'lda bo'lishi mumkin — yetkazib berishni aniqlang.",
                DedupKey = $"EsfNoTtn:doc:{d.Id}:{stamp}"
            }, ct);
        }
        return raised;
    }

    /// <summary>
    /// KUN BO'YI BITTA HAM HUJJAT KELMADI.
    ///
    /// Didox javob berayotgan bo'lishi mumkin, lekin bo'sh ro'yxat qaytarsa
    /// «aloqa uzildi» xabari chiqmaydi. Ish kuni tugab bitta ham hujjat
    /// kelmagan bo'lsa — sozlamada xato bo'lishi mumkin.
    /// </summary>
    private async Task<int> NoDocumentsTodayAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        // Faqat ish kunlari va kun oxirida.
        if (UzTime.Now.Hour < 18) return 0;
        if (today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return 0;

        var start = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), UzTime.Zone.BaseUtcOffset);
        if (await _db.Documents.AsNoTracking().AnyAsync(d => d.CreatedAt >= start, ct)) return 0;

        return await Raise(new Notification
        {
            Kind = NotificationKind.NoDocumentsToday,
            TargetRole = UserRole.Admin,
            Severity = NotificationSeverity.Warning,
            Title = "📭 BUGUN HUJJAT KELMADI",
            Body = $"{today:dd.MM.yyyy} — Didox'dan bitta ham hujjat tushmadi.\n\n" +
                   "Haqiqatan ish bo'lmagan bo'lishi mumkin.\n" +
                   "Yoki integratsiya sozlamasi buzilgan — tekshiring.",
            DedupKey = $"NoDocs:{today:yyyy-MM-dd}"
        }, ct);
    }

    // ══════════════════════════════════════════════════════════════
    //  OQIM BO'G'INLARI
    //
    //  Hujjat yuborilgani bilan ish tugamaydi — qarshi tomon javob
    //  berishi kerak. Kutish paytida oqim jimgina to'xtab qoladi.
    //  Har bo'g'in uchun alohida xabar, muddati bilan.
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// YUBORDIK — HALI QABUL QILINMADI.
    ///
    /// Eng og'rig'i doverennostda: u MUDDAT bilan chiqadi va muddat
    /// tugab ketsa tovar olib bo'lmaydi — hamma ish qaytadan boshlanadi.
    /// Shu sabab doverennost uchun muddatga qarab, qolganlari uchun
    /// yuborilgan kunidan hisoblanadi.
    /// </summary>
    private async Task<int> AwaitingAcceptanceAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        const int WaitDays = 2;    // shundan keyin eslatamiz
        var raised = 0;

        var rows = await _db.Documents.AsNoTracking()
            .Where(d => d.Direction == DocDirection.Out
                     && (d.Status == DocStatus.Sent || d.Status == DocStatus.Delivered)
                     && d.Deal!.Status == DealStatus.Active)
            .Select(d => new
            {
                d.Id, d.DocType, d.DocNumber, d.DocDate, d.DealId,
                Number = d.Deal!.Number, d.Deal.ManagerId,
                Who = d.Deal.Counterparty!.Name,
                Until = d.Doverennost != null ? (DateOnly?)d.Doverennost.ValidUntil : null,
            })
            .ToListAsync(ct);

        foreach (var d in rows)
        {
            var waiting = today.DayNumber - d.DocDate.DayNumber;
            if (waiting < WaitDays) continue;

            var label = DealWorkflow.DocLabel(d.DocType);

            // Doverennost — muddat asosiy o'lchov.
            string tail;
            NotificationSeverity sev;

            if (d.Until is { } until)
            {
                var left = until.DayNumber - today.DayNumber;
                if (left < 0)
                {
                    tail = $"Muddat {-left} kun oldin tugagan ({until:dd.MM.yyyy}).\n" +
                           "Bu doverennost bo'yicha tovar olib bo'lmaydi — yangisi kerak.";
                    sev = NotificationSeverity.Critical;
                }
                else
                {
                    tail = $"Muddat tugashiga {left} kun qoldi ({until:dd.MM.yyyy}).\n" +
                           "Muddat tugasa hammasi qaytadan boshlanadi.";
                    sev = left <= 3 ? NotificationSeverity.Critical : NotificationSeverity.Warning;
                }
            }
            else
            {
                tail = "Qarshi tomondan javob kutilmoqda.";
                sev = waiting > 7 ? NotificationSeverity.Critical : NotificationSeverity.Warning;
            }

            raised += await Raise(new Notification
            {
                Kind = NotificationKind.AwaitingAcceptance,
                TargetRole = UserRole.Perehod,
                Severity = sev,
                UserId = d.ManagerId,
                DealId = d.DealId,
                DocumentId = d.Id,
                Title = "⏳ QABUL QILINMADI",
                Body = $"Kontragent: {d.Who}\n" +
                       $"Shartnoma:  {d.Number}\n\n" +
                       $"{label} №{d.DocNumber ?? "—"} yuborilgan: {d.DocDate:dd.MM.yyyy} ({waiting} kun oldin)\n" +
                       "Hali qabul qilinmadi.\n\n" + tail,
                DedupKey = $"Awaiting:doc:{d.Id}:{stamp}"
            }, ct);
        }
        return raised;
    }

    /// <summary>
    /// QABUL QILINDI — LEKIN KEYINGI HUJJAT KELMADI.
    ///
    /// Doverennost qabul qilingach faktura kutiladi, fakturadan keyin TTN.
    /// Har bo'g'in orasida uzilish bo'lishi mumkin va uni faqat shu tekshiruv
    /// ko'radi: hujjat bor, zanjir davom etmayapti.
    /// </summary>
    private async Task<int> NextDocExpectedAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        const int WaitDays = 3;
        var raised = 0;

        var rows = await _db.Documents.AsNoTracking()
            .Where(d => d.Status == DocStatus.Accepted && d.Deal!.Status == DealStatus.Active)
            .Select(d => new
            {
                d.Id, d.DocType, d.DocNumber, d.DocDate, d.DealId,
                Number = d.Deal!.Number, d.Deal.ManagerId,
                Who = d.Deal.Counterparty!.Name,
                Have = d.Deal.Documents
                    .Where(x => x.Status != DocStatus.Cancelled && x.Status != DocStatus.Rejected)
                    .Select(x => x.DocType)
                    .ToList(),
            })
            .ToListAsync(ct);

        foreach (var d in rows)
        {
            if (DealWorkflow.NextInChain(d.DocType) is not { } next) continue;
            if (d.Have.Contains(next)) continue;

            var days = today.DayNumber - d.DocDate.DayNumber;
            if (days < WaitDays) continue;

            raised += await Raise(new Notification
            {
                Kind = NotificationKind.NextDocExpected,
                TargetRole = UserRole.Perehod,
                Severity = days > 10 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                UserId = d.ManagerId,
                DealId = d.DealId,
                DocumentId = d.Id,
                Title = $"📌 {DealWorkflow.DocLabel(next).ToUpperInvariant()} KUTILMOQDA",
                Body = $"Kontragent: {d.Who}\n" +
                       $"Shartnoma:  {d.Number}\n\n" +
                       $"{DealWorkflow.DocLabel(d.DocType)} №{d.DocNumber ?? "—"} qabul qilingan " +
                       $"({d.DocDate:dd.MM.yyyy}, {days} kun oldin).\n" +
                       $"Endi {DealWorkflow.DocLabel(next)} kelishi kerak edi — hali yo'q.\n\n" +
                       "Oqim shu yerda to'xtab turibdi.",
                DedupKey = $"NextDoc:doc:{d.Id}:{next}:{stamp}"
            }, ct);
        }
        return raised;
    }
    /// <summary>
    /// DOGOVOR KELDI, KEYIN HECH NARSA BO'LMADI.
    /// Shartnoma ochilgan, lekin na doverennost, na faktura — ish boshlanmagan.
    /// </summary>
    private async Task<int> ContractWithoutFollowUpAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        const int WaitDays = 7;
        var raised = 0;

        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active
                     && d.Documents.Count(x => x.Status != DocStatus.Cancelled) == 1
                     && d.Documents.Any(x => x.DocType == DocType.Contract))
            .Select(d => new { d.Id, d.Number, d.ManagerId, d.ContractDate, Who = d.Counterparty!.Name })
            .ToListAsync(ct);

        foreach (var d in rows)
        {
            if (d.ContractDate is not { } from) continue;
            var days = today.DayNumber - from.DayNumber;
            if (days < WaitDays) continue;

            raised += await Raise(new Notification
            {
                Kind = NotificationKind.ContractWithoutFollowUp,
                TargetRole = UserRole.Perehod,
                Severity = days > 30 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                UserId = d.ManagerId, DealId = d.Id,
                Title = "📄 DOGOVORDAN KEYIN HECH NARSA BO'LMADI",
                Body = $"Kontragent: {d.Who}\n" +
                       $"Shartnoma:  {d.Number}\n\n" +
                       $"Dogovor sanasi: {from:dd.MM.yyyy} ({days} kun oldin)\n" +
                       "Boshqa hujjat kelmadi — ish boshlanmagan.",
                DedupKey = $"NoFollowUp:deal:{d.Id}:{stamp}"
            }, ct);
        }
        return raised;
    }

    /// <summary>
    /// QARZ BOR, DOGOVOR YO'Q.
    /// Pul harakati bo'lgan, lekin asos hujjat yo'q — soliq tekshiruvida savol tug'diradi.
    /// </summary>
    private async Task<int> DebtWithoutContractAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;
        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active
                     && d.AmountTotal > 0
                     && !d.Documents.Any(x => x.DocType == DocType.Contract
                                           && x.Status != DocStatus.Cancelled))
            .Select(d => new { d.Id, d.Number, d.ManagerId, d.AmountTotal, d.AmountPaid, Who = d.Counterparty!.Name })
            .Take(50)
            .ToListAsync(ct);

        foreach (var d in rows)
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.DebtWithoutContract,
                TargetRole = UserRole.Perehod,
                Severity = NotificationSeverity.Warning,
                UserId = d.ManagerId, DealId = d.Id,
                Title = "📑 DOGOVOR HUJJATI YO'Q",
                Body = $"Kontragent: {d.Who}\n" +
                       $"Shartnoma:  {d.Number}\n\n" +
                       $"Summa:     {d.AmountTotal:N0} so'm\n" +
                       $"To'langan: {d.AmountPaid:N0} so'm\n\n" +
                       "Bu shartnoma bo'yicha dogovor hujjati kelmagan.\n" +
                       "Soliq tekshiruvida asos so'raladi.",
                DedupKey = $"NoContract:deal:{d.Id}:{stamp}"
            }, ct);
        return raised;
    }

    /// <summary>SHARTNOMA MUDDATI TUGAYAPTI — yangilash yoki yopish kerak.</summary>
    private async Task<int> ContractDueSoonAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        var soon = today.AddDays(7);
        var raised = 0;

        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active
                     && d.Steps.Any(x => x.Step == StepType.Payment
                                      && x.Status == StepStatus.Pending
                                      && x.DueDate != null && x.DueDate <= soon)
                     && d.AmountTotal > d.AmountPaid)
            .Select(d => new { d.Id, d.Number, d.ManagerId, d.AmountTotal, d.AmountPaid,
                               Who = d.Counterparty!.Name,
                               DueDate = d.Steps.Where(x => x.Step == StepType.Payment)
                                                .Select(x => x.DueDate).FirstOrDefault() })
            .ToListAsync(ct);

        foreach (var d in rows)
        {
            var left = d.DueDate!.Value.DayNumber - today.DayNumber;
            raised += await Raise(new Notification
            {
                Kind = NotificationKind.ContractDueSoon,
                TargetRole = UserRole.Realizatsiya,
                Severity = left < 0 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                UserId = d.ManagerId, DealId = d.Id,
                Title = left < 0 ? "📕 SHARTNOMA MUDDATI O'TDI" : "📙 SHARTNOMA MUDDATI TUGAYAPTI",
                Body = $"Kontragent: {d.Who}\n" +
                       $"Shartnoma:  {d.Number}\n\n" +
                       (left < 0 ? $"Muddat {-left} kun oldin tugagan" : $"Tugashiga {left} kun qoldi") +
                       $" ({d.DueDate:dd.MM.yyyy})\n" +
                       $"Yopilmagan qarz: {d.AmountTotal - d.AmountPaid:N0} so'm",
                DedupKey = $"DueSoon:deal:{d.Id}:{stamp}"
            }, ct);
        }
        return raised;
    }

    /// <summary>UZOQ JIM KONTRAGENT — ilgari faol edi, endi hujjat kelmayapti.</summary>
    private async Task<int> QuietCounterpartyAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        const int QuietDays = 60;
        var edge = DateTimeOffset.UtcNow.AddDays(-QuietDays);
        var raised = 0;

        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active && d.AmountTotal > d.AmountPaid)
            .GroupBy(d => new { d.CounterpartyId, Name = d.Counterparty!.Name })
            .Select(g => new
            {
                Id = g.Key.CounterpartyId,
                g.Key.Name,
                Last = g.SelectMany(d => d.Documents).Max(x => (DateTimeOffset?)x.CreatedAt),
                Debt = g.Sum(d => d.AmountTotal - d.AmountPaid),
            })
            .ToListAsync(ct);

        foreach (var c in rows)
        {
            if (c.Last is not { } last || last > edge) continue;
            var days = (int)(DateTimeOffset.UtcNow - last).TotalDays;

            raised += await Raise(new Notification
            {
                Kind = NotificationKind.QuietCounterparty,
                TargetRole = UserRole.Realizatsiya,
                Severity = NotificationSeverity.Warning,
                Title = "🔕 KONTRAGENTDAN UZOQ VAQT HUJJAT YO'Q",
                Body = $"Kontragent: {c.Name}\n\n" +
                       $"Oxirgi hujjat: {days} kun oldin\n" +
                       $"Yopilmagan qarz: {c.Debt:N0} so'm\n\n" +
                       "Ish to'xtaganmi yoki hujjatlar Didox'ga tushmayaptimi — aniqlang.",
                DedupKey = $"Quiet:cp:{c.Id}:{stamp}"
            }, ct);
        }
        return raised;
    }


    private async Task<int> Raise(Notification n, CancellationToken ct)
        => await _notify.RaiseAsync(n, ct) ? 1 : 0;
}

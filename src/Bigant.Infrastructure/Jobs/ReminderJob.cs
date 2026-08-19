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
                    Severity = NotificationSeverity.Critical,
                    UserId = d.ManagerId,
                    DealId = d.Id,
                    Title = $"Pul keldi, tovar berilmadi — {d.Number}",
                    Body = $"{who}: {d.AmountPaid:N0} so'm to'langan, TTN hali yo'q. {d.AgeDays} kun bo'ldi.",
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
                        Severity = daysSince > grace + 7
                            ? NotificationSeverity.Critical
                            : NotificationSeverity.Warning,
                        UserId = d.ManagerId,
                        DealId = d.Id,
                        Title = $"To'lov kelmadi — {d.Number}",
                        Body = $"{who}: tovar {daysSince} kun oldin berilgan, " +
                               $"{d.AmountUnpaid:N0} so'm kelmagan (otsrochka {grace} kun).",
                        DedupKey = $"ShippedNotPaid:deal:{d.Id}:{stamp}"
                    }, ct);
                }
            }

            // ── 3. Muddati o'tgan cheklist qadamlari ──
            foreach (var s in d.Steps.Where(s => s.Status == StepStatus.Pending
                                              && s.DueDate is { } dd && dd < today))
            {
                var late = today.DayNumber - s.DueDate!.Value.DayNumber;
                raised += await Raise(new Notification
                {
                    Kind = NotificationKind.StepOverdue,
                    Severity = late > 7 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                    UserId = d.ManagerId,
                    DealId = d.Id,
                    Title = $"{DealWorkflow.StepLabel(s.Step)} kechikdi — {d.Number}",
                    Body = $"{who}: \"{DealWorkflow.StepLabel(s.Step)}\" qadami {late} kun kechikdi.",
                    DedupKey = $"StepOverdue:deal:{d.Id}:{s.Step}:{stamp}"
                }, ct);
            }
        }

        raised += await DoverennostExpiryAsync(today, stamp, ct);
        raised += await ChainBreakAsync(stamp, ct);

        if (raised > 0) _log.LogInformation("Eslatma motori: {Count} ta yangi eslatma", raised);
        await _notify.PushPendingAsync(ct);
    }

    /// <summary>
    /// ZANJIR UZILISHI. Egasining asosiy talabi:
    ///   «aynan o'sha dogovorga doverennost chiqarildimi,
    ///    aynan o'sha doverennostga faktura chiqarildimi».
    /// Faktura kelib, unga asos bo'lgan doverennost topilmasa — bu jiddiy
    /// nosozlik: tovar hujjatsiz berilgan yoki hujjat noto'g'ri bitimga tushgan.
    /// </summary>
    private async Task<int> ChainBreakAsync(string stamp, CancellationToken ct)
    {
        var raised = 0;

        var rows = await _db.Documents.AsNoTracking()
            .Where(x => x.DealId != null
                     && x.Deal!.Status == DealStatus.Active
                     && x.Status != DocStatus.Cancelled && x.Status != DocStatus.Rejected)
            .Select(x => new
            {
                DealId = x.DealId!.Value,
                x.Deal!.Number,
                Who = x.Deal.Counterparty!.Name,
                ManagerId = x.Deal.ManagerId,
                x.Id, x.DocType, x.ParentDocumentId
            })
            .ToListAsync(ct);

        foreach (var g in rows.GroupBy(r => r.DealId))
        {
            var breaks = DealWorkflow.CheckChain(
                g.Select(x => (x.DocType, x.Id, x.ParentDocumentId)));
            if (breaks.Count == 0) continue;

            var first = g.First();
            var text = string.Join("\n", breaks.Select(b => "• " + b.Problem));

            raised += await Raise(new Notification
            {
                Kind = NotificationKind.IncomingDocument,
                Severity = NotificationSeverity.Critical,
                UserId = first.ManagerId,
                DealId = first.DealId,
                Title = "TEKSHIRING — hujjat zanjiri buzilgan",
                Body = $"Kontragent: {first.Who}\n" +
                       $"Shartnoma:  {first.Number}\n\n" +
                       $"Muammo:\n{text}\n\n" +
                       "Tovar ishonchnomasiz berilgan bo'lishi mumkin.\n" +
                       $"Batafsil: /zanjir {first.Number}",
                DedupKey = $"ChainBreak:deal:{first.DealId}:{breaks.Count}:{stamp}"
            }, ct);
        }

        return raised;
    }

    /// <summary>
    /// DOVERENNOST MUDDATI. Ishonchnoma cheklangan muddatga beriladi —
    /// muddat o'tsa ekspeditor tovarni ololmaydi va yo'lda qoladi.
    /// Shuning uchun 3 kun oldin ogohlantiramiz, o'tib ketsa qizil signal.
    /// </summary>
    private async Task<int> DoverennostExpiryAsync(DateOnly today, string stamp, CancellationToken ct)
    {
        var soon = today.AddDays(3);
        var raised = 0;

        var rows = await _db.Doverennosti
            .AsNoTracking()
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
                Kind = NotificationKind.StepOverdue,
                Severity = expired ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                UserId = deal.ManagerId,
                DealId = deal.Id,
                DocumentId = d.DocumentId,
                Title = expired
                    ? $"Doverennost muddati O'TDI — {deal.Number}"
                    : $"Doverennost muddati tugayapti — {deal.Number}",
                Body = expired
                    ? $"{who}: {d.EmployeeName} nomidagi ishonchnoma {days} kun oldin tugagan " +
                      $"({d.ValidUntil:dd.MM.yyyy}). Tovar hali olinmagan — yangi doverennost kerak."
                    : $"{who}: {d.EmployeeName} nomidagi ishonchnoma {days} kundan keyin tugaydi " +
                      $"({d.ValidUntil:dd.MM.yyyy}). Tovar hali olinmagan.",
                DedupKey = $"DovExpiry:doc:{d.DocumentId}:{stamp}"
            }, ct);
        }

        return raised;
    }

    private async Task<int> Raise(Notification n, CancellationToken ct)
        => await _notify.RaiseAsync(n, ct) ? 1 : 0;
}

using Bigant.Domain;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Infrastructure.Jobs;

/// <summary>
/// ERTALABKI XULOSA — kuniga bir marta.
/// Rahbar va bugalter bitta xabarda butun manzarani ko'radi.
/// </summary>
public sealed class DigestJob
{
    private readonly AppDbContext _db;
    private readonly IDashboardService _dash;
    private readonly INotificationService _notify;

    public DigestJob(AppDbContext db, IDashboardService dash, INotificationService notify)
    {
        _db = db; _dash = dash; _notify = notify;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var today = UzTime.Today;
        var yesterday = today.AddDays(-1);
        var stamp = today.ToString("yyyy-MM-dd");

        // ── Rahbar va bugalterga umumiy xulosa ──
        var s = await _dash.SummaryAsync(yesterday, yesterday, null, ct);

        var body =
            $"Kecha yangi shartnoma: {s.NewDeals} ta\n" +
            $"Yopilgan: {s.ClosedDeals} ta\n" +
            $"Hozir ochiq: {s.OpenDeals} ta ({s.OpenAmount:N0} so'm)\n" +
            $"\n" +
            $"Bizga qarz (debitor): {s.ReceivableTotal:N0}\n" +
            $"Biz qarzdormiz (kreditor): {s.PayableTotal:N0}\n" +
            $"\n" +
            $"Pul keldi, tovar yo'q: {s.PaidNotShipped} ta\n" +
            $"Tovar berildi, pul yo'q: {s.ShippedNotPaid} ta\n" +
            $"Muddati o'tgan qadam: {s.OverdueSteps} ta";

        var bosses = await _db.Users
            .Where(u => u.IsActive && (u.Role == UserRole.Manager || u.Role == UserRole.Accountant
                                                                 || u.Role == UserRole.Admin))
            .ToListAsync(ct);

        foreach (var u in bosses)
        {
            await _notify.RaiseAsync(new Notification
            {
                Kind = NotificationKind.DailyDigest,
                Severity = s.PaidNotShipped > 0 ? NotificationSeverity.Warning : NotificationSeverity.Info,
                UserId = u.Id,
                Title = $"Kunlik xulosa — {today:dd.MM.yyyy}",
                Body = body,
                DedupKey = $"Digest:user:{u.Id}:{stamp}"
            }, ct);
        }

        await _notify.PushPendingAsync(ct);
    }
}

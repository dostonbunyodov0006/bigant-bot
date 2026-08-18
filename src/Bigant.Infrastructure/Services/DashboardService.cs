using Bigant.Domain;
using Bigant.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Infrastructure.Services;

public sealed record DashboardSummary(
    DateOnly From,
    DateOnly To,
    int NewDeals,
    int ClosedDeals,
    int OpenDeals,
    decimal OpenAmount,
    decimal ReceivableTotal,        // klientlar bizga qarzdor
    decimal PayableTotal,           // biz postavshiklarga qarzdormiz
    int PaidNotShipped,
    int ShippedNotPaid,
    int OverdueSteps,
    int UnreadNotifications);

public sealed record OpenDealRow(
    long Id, string Number, string Kind, string Counterparty, string? Manager,
    decimal Amount, decimal Unpaid, int AgeDays, string NextStep, bool IsOverdue, string Flag);

public interface IDashboardService
{
    Task<DashboardSummary> SummaryAsync(DateOnly from, DateOnly to, long? userId, CancellationToken ct = default);
    Task<IReadOnlyList<OpenDealRow>> OpenDealsAsync(long? userId, int take = 100, CancellationToken ct = default);
}

public sealed class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;
    public DashboardService(AppDbContext db) => _db = db;

    public async Task<DashboardSummary> SummaryAsync(DateOnly from, DateOnly to, long? userId,
        CancellationToken ct = default)
    {
        // Sanalar Toshkent vaqti bo'yicha, bazadagi belgilar UTC — to'g'ri o'girish shart.
        var (fromTs, toTs) = UzTime.DayRangeUtc(from, to);
        var today = UzTime.Today;

        var scoped = _db.Deals.AsNoTracking()
            .Where(d => userId == null || d.ManagerId == userId);

        var inPeriod = scoped.Where(d => d.CreatedAt >= fromTs && d.CreatedAt < toTs);

        var newDeals = await inPeriod.CountAsync(ct);
        var closedDeals = await scoped.CountAsync(d =>
            d.ClosedAt >= fromTs && d.ClosedAt < toTs, ct);

        var open = scoped.Where(d => d.Status == DealStatus.Active);
        var openDeals = await open.CountAsync(ct);
        var openAmount = await open.SumAsync(d => (decimal?)d.AmountTotal, ct) ?? 0m;

        var receivable = await open.Where(d => d.Kind == DealKind.Sale)
            .SumAsync(d => (decimal?)(d.AmountTotal - d.AmountPaid), ct) ?? 0m;
        var payable = await open.Where(d => d.Kind == DealKind.Purchase)
            .SumAsync(d => (decimal?)(d.AmountTotal - d.AmountPaid), ct) ?? 0m;

        // Pul keldi, tovar berilmadi — eng shoshilinch.
        var paidNotShipped = await open
            .Where(d => d.Kind == DealKind.Sale
                && d.Steps.Any(s => s.Step == StepType.Payment && s.Status == StepStatus.Done)
                && d.Steps.Any(s => s.Step == StepType.Ttn && s.Status == StepStatus.Pending))
            .CountAsync(ct);

        // Tovar berildi, pul kelmadi — debitor qarz.
        var shippedNotPaid = await open
            .Where(d => d.Kind == DealKind.Sale
                && d.Steps.Any(s => s.Step == StepType.Ttn && s.Status == StepStatus.Done)
                && d.Steps.Any(s => s.Step == StepType.Payment && s.Status == StepStatus.Pending))
            .CountAsync(ct);

        var overdueSteps = await _db.DealSteps.AsNoTracking()
            .Where(s => s.Status == StepStatus.Pending
                     && s.DueDate != null && s.DueDate < today
                     && s.Deal.Status == DealStatus.Active
                     && (userId == null || s.Deal.ManagerId == userId))
            .CountAsync(ct);

        // userId null = rahbar/bugalter ko'rinishi — barcha o'qilmaganlar sanaladi.
        // Aks holda faqat egasiz eslatmalar hisoblanib, 0 chiqib qolardi.
        var unread = userId is null
            ? await _db.Notifications.AsNoTracking().CountAsync(n => !n.IsRead, ct)
            : await _db.Notifications.AsNoTracking()
                .CountAsync(n => !n.IsRead && (n.UserId == null || n.UserId == userId), ct);

        return new DashboardSummary(from, to, newDeals, closedDeals, openDeals, openAmount,
            receivable, payable, paidNotShipped, shippedNotPaid, overdueSteps, unread);
    }

    public async Task<IReadOnlyList<OpenDealRow>> OpenDealsAsync(long? userId, int take = 100,
        CancellationToken ct = default)
    {
        var today = UzTime.Today;

        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active && (userId == null || d.ManagerId == userId))
            .OrderBy(d => d.CreatedAt)
            .Take(take)
            .Select(d => new
            {
                d.Id, d.Number, d.Kind, d.AmountTotal, d.AmountPaid, d.CreatedAt,
                Counterparty = d.Counterparty.Name,
                Manager = d.Manager != null ? d.Manager.FullName : null,
                Next = d.Steps.Where(s => s.Status == StepStatus.Pending)
                              .OrderBy(s => s.SortOrder)
                              .Select(s => new { s.Step, s.DueDate })
                              .FirstOrDefault(),
                PaidDone = d.Steps.Any(s => s.Step == StepType.Payment && s.Status == StepStatus.Done),
                TtnDone = d.Steps.Any(s => s.Step == StepType.Ttn && s.Status == StepStatus.Done)
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var flag = r.Kind == DealKind.Sale && r.PaidDone && !r.TtnDone ? "PAID_NOT_SHIPPED"
                     : r.Kind == DealKind.Sale && r.TtnDone && !r.PaidDone ? "SHIPPED_NOT_PAID"
                     : "";

            return new OpenDealRow(
                r.Id, r.Number, r.Kind.ToString(), r.Counterparty, r.Manager,
                r.AmountTotal, r.AmountTotal - r.AmountPaid,
                (int)(DateTimeOffset.UtcNow - r.CreatedAt).TotalDays,
                r.Next is null ? "—" : Domain.Workflow.DealWorkflow.StepLabel(r.Next.Step),
                r.Next?.DueDate is { } dd && dd < today,
                flag);
        }).ToList();
    }
}

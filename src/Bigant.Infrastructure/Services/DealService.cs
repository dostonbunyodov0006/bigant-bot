using Bigant.Domain;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bigant.Infrastructure.Services;

public sealed record NewDealItem(long ProductId, decimal Qty, decimal Price, long? SourceSupplierId);

public sealed record NewDealRequest(
    DealKind Kind,
    long CounterpartyId,
    long? ParentDealId,
    DateOnly? ContractDate,
    DateOnly? DueDate,
    /// <summary>Dogovorni biz tuzamizmi. null = standart qoida (sotuvda biz, xaridda ular).</summary>
    bool? ContractByUs,
    /// <summary>TTN ni biz rasmiylashtiramizmi. null = standart qoida.</summary>
    bool? TtnByUs,
    string? Comment,
    List<NewDealItem> Items);

public interface IDealService
{
    Task<Deal> ActivateAsync(long dealId, AppUser actor, CancellationToken ct = default);
    Task<Deal> CompleteStepAsync(long dealId, StepType step, AppUser actor,
                                 long? documentId = null, string? note = null, CancellationToken ct = default);
    Task<Deal> SkipStepAsync(long dealId, StepType step, AppUser actor, string reason, CancellationToken ct = default);
    Task<Deal> CancelAsync(long dealId, AppUser actor, string reason, CancellationToken ct = default);
    Task<Deal?> GetAsync(long dealId, CancellationToken ct = default);
}

public sealed class DealService : IDealService
{
    private readonly AppDbContext _db;
    private readonly INumberGenerator _numbers;
    private readonly INotificationService _notify;
    private readonly IAuditService _audit;
    private readonly Bigant.Didox.IDidoxClient _didox;
    private readonly ILogger<DealService> _log;

    public DealService(AppDbContext db, INumberGenerator numbers, INotificationService notify,
                       IAuditService audit, Bigant.Didox.IDidoxClient didox, ILogger<DealService> log)
    {
        _db = db; _numbers = numbers; _notify = notify; _audit = audit; _didox = didox; _log = log;
    }

    public Task<Deal?> GetAsync(long dealId, CancellationToken ct = default) =>
        _db.Deals
           .Include(d => d.Counterparty)
           .Include(d => d.Manager)
           .Include(d => d.Steps)
           .Include(d => d.Documents).ThenInclude(x => x.ParentDocument)
           .Include(d => d.Payments)
           .AsSplitQuery()
           .FirstOrDefaultAsync(d => d.Id == dealId, ct);

    // ══════════════════════════════════════════════════════════════
    //  YARATISH
    // ══════════════════════════════════════════════════════════════
    public async Task<Deal> ActivateAsync(long dealId, AppUser actor, CancellationToken ct = default)
    {
        var deal = await LoadAsync(dealId, ct);
        if (deal.Status != DealStatus.Draft)
            throw new BusinessException("BAD_STATUS", "Faqat qoralama shartnoma ishga tushiriladi");

        deal.Status = DealStatus.Active;
        _audit.Write(actor.Id, nameof(Deal), deal.Id, "activate");
        await _db.SaveChangesAsync(ct);
        return deal;
    }

    public async Task<Deal> CompleteStepAsync(long dealId, StepType step, AppUser actor,
        long? documentId = null, string? note = null, CancellationToken ct = default)
    {
        var deal = await LoadAsync(dealId, ct);
        if (deal.Status is DealStatus.Cancelled)
            throw new BusinessException("CANCELLED", "Bekor qilingan shartnoma o'zgartirilmaydi");

        var s = deal.Steps.FirstOrDefault(x => x.Step == step)
                ?? throw new BusinessException("NO_STEP", "Bunday qadam yo'q");

        if (s.Status == StepStatus.Done) return deal;   // idempotent

        s.Status = StepStatus.Done;
        s.CompletedAt = DateTimeOffset.UtcNow;
        s.DocumentId = documentId ?? s.DocumentId;
        s.Note = note ?? s.Note;

        if (deal.Status == DealStatus.Draft) deal.Status = DealStatus.Active;

        _audit.Write(actor.Id, nameof(DealStep), s.Id, "complete",
            null, new { deal.Number, Step = step.ToString(), documentId });

        await TryCloseAsync(deal, actor, ct);
        await _db.SaveChangesAsync(ct);
        return deal;
    }

    public async Task<Deal> SkipStepAsync(long dealId, StepType step, AppUser actor, string reason,
        CancellationToken ct = default)
    {
        var deal = await LoadAsync(dealId, ct);
        var s = deal.Steps.FirstOrDefault(x => x.Step == step)
                ?? throw new BusinessException("NO_STEP", "Bunday qadam yo'q");

        if (s.IsRequired && actor.Role != UserRole.Admin)
            throw new BusinessException("FORBIDDEN", "Majburiy qadamni faqat rahbar o'tkazib yuboradi");

        s.Status = StepStatus.Skipped;
        s.Note = reason;
        s.CompletedAt = DateTimeOffset.UtcNow;

        _audit.Write(actor.Id, nameof(DealStep), s.Id, "skip", null, new { reason });
        await TryCloseAsync(deal, actor, ct);
        await _db.SaveChangesAsync(ct);
        return deal;
    }

    public async Task<Deal> CancelAsync(long dealId, AppUser actor, string reason, CancellationToken ct = default)
    {
        if (actor.Role is not (UserRole.Realizatsiya or UserRole.Perehod or UserRole.Admin))
            throw new BusinessException("FORBIDDEN", "Shartnomani bekor qilishga huquqingiz yo'q");

        var deal = await LoadAsync(dealId, ct);
        if (deal.Status == DealStatus.Closed)
            throw new BusinessException("CLOSED", "Yopilgan shartnoma bekor qilinmaydi");

        deal.Status = DealStatus.Cancelled;
        deal.Comment = string.IsNullOrWhiteSpace(deal.Comment)
            ? $"Bekor qilindi: {reason}"
            : $"{deal.Comment}\nBekor qilindi: {reason}";

        _audit.Write(actor.Id, nameof(Deal), deal.Id, "cancel", null, new { reason });
        await _db.SaveChangesAsync(ct);
        return deal;
    }

    // ══════════════════════════════════════════════════════════════
    //  YORDAMCHILAR
    // ══════════════════════════════════════════════════════════════
    private async Task<Deal> LoadAsync(long id, CancellationToken ct) =>
        await _db.Deals
            .Include(d => d.Counterparty)
            .Include(d => d.Steps)
            .FirstOrDefaultAsync(d => d.Id == id, ct)
        ?? throw new BusinessException("NO_DEAL", "Shartnoma topilmadi");

    private async Task TryCloseAsync(Deal deal, AppUser actor, CancellationToken ct)
    {
        if (!DealWorkflow.ShouldClose(deal)) return;

        deal.Status = DealStatus.Closed;
        deal.ClosedAt = DateTimeOffset.UtcNow;
        _audit.Write(actor.Id, nameof(Deal), deal.Id, "close");

        await _notify.RaiseAsync(new Notification
        {
            Kind = NotificationKind.DailyDigest,
            Severity = NotificationSeverity.Info,
            UserId = deal.ManagerId,
            DealId = deal.Id,
            Title = $"Shartnoma {deal.Number} yopildi",
            Body = $"{deal.Counterparty?.Name} — {deal.AmountTotal:N0} so'm. Barcha hujjatlar to'liq.",
            DedupKey = $"Closed:deal:{deal.Id}"
        }, ct);
    }

    /// <summary>
    /// Standart shartnoma bo'limlarini shablondan ko'chiradi va o'rinbosarlarni to'ldiradi.
    /// Didox dogovorni Parts[] siz qabul qilmaydi, bugalter esa har safar
    /// bir xil matnni qayta yozmasligi kerak.
    /// </summary>
    private async Task ApplyContractTemplateAsync(Deal deal, Counterparty cp, CancellationToken ct)
    {
        var forSale = deal.Kind == DealKind.Sale;

        var tpl = await _db.ContractTemplates
            .Include(t => t.Parts)
            .Where(t => t.IsActive && (t.ForSale == null || t.ForSale == forSale))
            .OrderByDescending(t => t.ForSale != null)   // aniq moslashgani ustun
            .ThenByDescending(t => t.IsDefault)
            .FirstOrDefaultAsync(ct);

        if (tpl is null || tpl.Parts.Count == 0) return;

        var us = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "Bigant";

        var vars = new Dictionary<string, string>
        {
            ["{BIZ}"]             = us,
            ["{KLIENT}"]          = cp.Name,
            ["{SUMMA}"]           = deal.AmountTotal.ToString("N0"),
            ["{SHARTNOMA_RAQAM}"] = deal.Number,
            ["{SANA}"]            = (deal.ContractDate ?? UzTime.Today).ToString("dd.MM.yyyy"),
            ["{MUDDAT}"]          = (deal.PaymentDays > 0 ? deal.PaymentDays : cp.PaymentDays).ToString()
        };

        foreach (var p in tpl.Parts.OrderBy(p => p.OrdNo))
        {
            var body = p.Body;
            foreach (var (k, v) in vars) body = body.Replace(k, v);

            deal.Parts.Add(new ContractPart { OrdNo = p.OrdNo, Title = p.Title, Body = body });
        }

        // Didox majburiy maydonlari — berilmagan bo'lsa oqilona standart.
        deal.ContractName ??= forSale
            ? "Tovar yetkazib berish shartnomasi"
            : "Tovar sotib olish shartnomasi";
        deal.ContractPlace ??= "Toshkent shahri";
        deal.ContractExpireDate ??= (deal.ContractDate ?? UzTime.Today).AddMonths(12);
    }


}

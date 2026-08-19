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
    Task<Deal> CreateAsync(NewDealRequest req, AppUser actor, CancellationToken ct = default);
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
           .Include(d => d.Items).ThenInclude(i => i.Product)
           .Include(d => d.Steps)
           .Include(d => d.Documents).ThenInclude(x => x.ParentDocument)
           .Include(d => d.Payments)
           .AsSplitQuery()
           .FirstOrDefaultAsync(d => d.Id == dealId, ct);

    // ══════════════════════════════════════════════════════════════
    //  YARATISH
    // ══════════════════════════════════════════════════════════════
    public async Task<Deal> CreateAsync(NewDealRequest req, AppUser actor, CancellationToken ct = default)
    {
        if (req.Items is null or { Count: 0 })
            throw new BusinessException("NO_ITEMS", "Shartnomada kamida bitta tovar bo'lishi kerak");

        var cp = await _db.Counterparties.FirstOrDefaultAsync(c => c.Id == req.CounterpartyId, ct)
                 ?? throw new BusinessException("NO_COUNTERPARTY", "Kontragent topilmadi");

        if (cp.IsBlocked)
            throw new BusinessException("BLOCKED",
                $"\"{cp.Name}\" bloklangan: {cp.BlockReason ?? "sabab ko'rsatilmagan"}");

        if (req.Kind == DealKind.Sale && !cp.IsClient)
            throw new BusinessException("NOT_CLIENT", $"\"{cp.Name}\" klient sifatida belgilanmagan");
        if (req.Kind == DealKind.Purchase && !cp.IsSupplier)
            throw new BusinessException("NOT_SUPPLIER", $"\"{cp.Name}\" postavshik sifatida belgilanmagan");

        var productIds = req.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Include(p => p.Group)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        if (products.Count != productIds.Count)
            throw new BusinessException("NO_PRODUCT", "Ba'zi tovarlar topilmadi");

        // Prays-listlar у.е. da kelgan. Joriy kursni bir marta olamiz va
        // shartnomaga MUZLATAMIZ — kurs keyin o'zgarsa chiqarilgan faktura
        // o'zgarmasligi kerak.
        var rate = await _db.ExchangeRates.AsNoTracking()
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(ct);

        if (products.Values.Any(p => p.Currency == "USD") && rate is null)
            throw new BusinessException("NO_RATE",
                "Tovar narxi у.е. da, lekin valyuta kursi kiritilmagan. " +
                "Sozlamalar → Kurs bo'limiga kirib joriy kursni qo'ying.");

        var deal = new Deal
        {
            Kind = req.Kind,
            Number = await _numbers.NextAsync(req.Kind == DealKind.Sale ? "S" : "P", ct),
            CounterpartyId = cp.Id,
            ManagerId = actor.Id,
            Status = DealStatus.Draft,
            ParentDealId = req.ParentDealId,
            ContractByUs = req.ContractByUs,
            TtnByUs = req.TtnByUs,
            // To'lov sharti kontragent kartochkasidan meros oladi.
            PaymentDays = cp.PaymentDays,
            ContractDate = req.ContractDate ?? UzTime.Today,
            DueDate = req.DueDate,
            Comment = req.Comment
        };

        foreach (var line in req.Items)
        {
            if (line.Qty <= 0)
                throw new BusinessException("BAD_QTY", "Miqdor musbat bo'lishi kerak");
            if (line.Price < 0)
                throw new BusinessException("BAD_PRICE", "Narx manfiy bo'lishi mumkin emas");

            var p = products[line.ProductId];
            if (!p.IsActive)
                throw new BusinessException("INACTIVE_PRODUCT", $"\"{p.Name}\" katalogda faol emas");

            // MXIK — faqat Didox yoqilganda MAJBURIY.
            // O'chiq bo'lsa hujjat rasman ketmaydi, shuning uchun to'sib turish
            // ortiqcha: katalog to'ldirilmaguncha tizimni ishlatib bo'lmasdi.
            // Ogohlantirish "Didox tayyorligi" ekranida ko'rinadi.
            if (_didox.IsEnabled && string.IsNullOrWhiteSpace(p.EffectiveMxik))
                throw new BusinessException("NO_MXIK",
                    $"\"{p.Name}\" uchun MXIK kodi kiritilmagan — schyot-faktura chiqarib bo'lmaydi");

            // Sotuvchi narxni DOIM so'mda kiritadi — klient so'm to'laydi.
            // Katalogdagi narx у.е. da bo'lsa, uni ham so'mga o'girib solishtiramiz.
            var isUsd = p.Currency == "USD";
            var basePriceUzs = isUsd ? Math.Round(p.BasePrice * rate!.Value, 2) : p.BasePrice;

            if (req.Kind == DealKind.Sale)
                ValidateSalePrice(p, basePriceUzs, line.Price, actor);

            deal.Items.Add(new DealItem
            {
                ProductId = p.Id,
                Qty = line.Qty,
                Price = line.Price,
                // Kursni muzlatamiz — keyin «qaysi kurs bilan sotilgan» deb aniqlash uchun.
                RateSnapshot = isUsd ? rate : null,
                VatRate = p.EffectiveVatRate,
                MxikSnapshot = p.EffectiveMxik,
                UnitSnapshot = p.Unit
            });
        }

        Recalculate(deal);

        deal.Steps.AddRange(DealWorkflow.BuildSteps(
            req.Kind, deal.ContractDate ?? UzTime.Today, cp.PaymentDays));

        // Dogovor bo'limlarini shablondan ko'chiramiz — bugalter qayta yozmasin.
        await ApplyContractTemplateAsync(deal, cp, ct);

        // Tranzaksiya retry strategiyasi ichida bajariladi — aks holda
        // ulanish uzilib qayta urinilganda yarim yozilgan shartnoma qoladi.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            _db.Deals.Add(deal);
            await _db.SaveChangesAsync(ct);

            _audit.Write(actor.Id, nameof(Deal), deal.Id, "create", null, new
            {
                deal.Number, deal.Kind, deal.CounterpartyId, deal.AmountTotal
            });
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        });

        _log.LogInformation("Shartnoma yaratildi {Number} ({Amount})", deal.Number, deal.AmountTotal);
        return deal;
    }

    // ══════════════════════════════════════════════════════════════
    //  HOLAT O'ZGARISHLARI
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
            .Include(d => d.Items)
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

    private static void Recalculate(Deal deal)
    {
        deal.AmountTotal = deal.Items.Sum(i => i.Amount);
    }

    /// <summary>
    /// Narx qoidasi — bittagina: qat'iy narxli tovarni sotuvchi o'zgartira olmaydi.
    ///
    /// Egasining qarori (2026-08-18): chegirma chegarasi va
    /// kredit limiti OLIB TASHLANDI. Tizim savdo qoidalarini nazorat qilmaydi —
    /// uning vazifasi hujjat chiqarish. Narx to'g'riligini odam hal qiladi.
    /// </summary>
    private static void ValidateSalePrice(Product p, decimal basePriceUzs, decimal price, AppUser actor)
    {
        var isBoss = actor.Role == UserRole.Admin;

        // 1 so'mgacha farqqa yo'l qo'yamiz — kurs konvertatsiyasida yaxlitlash bo'ladi.
        if (p.IsPriceFixed && Math.Abs(price - basePriceUzs) > 1m && !isBoss)
            throw new BusinessException("FIXED_PRICE",
                $"\"{p.Name}\" qat'iy narxda sotiladi: {basePriceUzs:N0} so'm. " +
                "O'zgartirish uchun rahbar tasdig'i kerak");
    }
}

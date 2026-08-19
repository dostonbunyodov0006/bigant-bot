using Bigant.Domain;
using Bigant.Api.Auth;
using Bigant.Domain.Enums;
using Bigant.Domain.Workflow;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

/// <summary>
/// ROL BO'YICHA HISOBOT EKRANLARI.
///
/// Dashboard — ma'lumot KIRITISH joyi emas, bo'layotgan ishni KO'RISH joyi.
/// Shuning uchun bu yerdagi hamma narsa faqat o'qish.
///
///   /api/view/realizatsiya — «Nima sotdik, kimga, qancha, puli keldimi?»
///   /api/view/perehod     — «Qaysi hujjat qaysiga tegishli, zanjir butunmi?»
///
/// Admin ikkalasini ham ko'radi.
/// </summary>
public static class RoleDashboardEndpoints
{
    public static void MapRoleDashboardEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/view").WithTags("Rol ekranlari").RequireAuthorization().RequireRateLimiting("api");

        // ══════════════════════════════════════════════════════════
        //  REALIZATSIYA — sotuv buxgalteri
        // ══════════════════════════════════════════════════════════
        g.MapGet("/realizatsiya", async (AppDbContext db, CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (actor.Role is not (UserRole.Realizatsiya or UserRole.Admin))
                throw new BusinessException("FORBIDDEN", "Bu bo'lim realizatsiya buxgalteri uchun");

            var today = UzTime.Today;
            var monthAgo = today.AddDays(-30);
            var (fromTs, _) = UzTime.DayRangeUtc(monthAgo, today);

            // Faqat SOTUV bitimlari — realizatsiya xaridni ko'rmaydi.
            var sales = db.Deals.AsNoTracking().Where(d => d.Kind == DealKind.Sale);

            var open = await sales.Where(d => d.Status == DealStatus.Active).ToListAsync(ct);
            var closedCount = await sales.CountAsync(
                d => d.Status == DealStatus.Closed && d.ClosedAt >= fromTs, ct);

            // ── Kimga nima sotdik ──
            var rows = await sales
                .Where(d => d.Status == DealStatus.Active || d.ClosedAt >= fromTs)
                .OrderByDescending(d => d.CreatedAt)
                .Take(100)
                .Select(d => new
                {
                    d.Id,
                    d.Number,
                    Counterparty = d.Counterparty.Name,
                    Inn = d.Counterparty.Inn,
                    d.AmountTotal,
                    d.AmountPaid,
                    Debt = d.AmountTotal - d.AmountPaid,
                    // QQS Didox hujjatidan keladi — tovar ro'yxati bizda yo'q.
                    Vat = d.Documents.Where(x => x.DocType == DocType.Esf).Sum(x => x.VatAmount),
                    Status = d.Status.ToString(),
                    d.ContractDate,
                    d.DueDate,
                    // Hujjat holati — realizatsiya shularni tekshiradi
                    HasEsf = d.Documents.Any(x => x.DocType == DocType.Esf
                                               && x.Status != DocStatus.Cancelled),
                    HasTtn = d.Documents.Any(x => x.DocType == DocType.Ttn
                                               && x.Status != DocStatus.Cancelled),
                })
                .ToListAsync(ct);

            // ── Tekshirish kerak bo'lgan joylar ──
            var noInn = rows.Where(r => string.IsNullOrWhiteSpace(r.Inn)).Select(r => r.Number).ToList();

            var overdue = rows
                .Where(r => r.Debt > 0 && r.DueDate != null && r.DueDate < today)
                .OrderByDescending(r => r.Debt)
                .Take(20)
                .Select(r => new
                {
                    r.Number, r.Counterparty, r.Debt,
                    LateDays = today.DayNumber - r.DueDate!.Value.DayNumber
                })
                .ToList();

            return Results.Ok(new
            {
                role = "Realizatsiya",
                period = new { from = monthAgo, to = today },
                summary = new
                {
                    openDeals = open.Count,
                    closedDeals = closedCount,
                    soldTotal = rows.Sum(r => r.AmountTotal),
                    receivedTotal = rows.Sum(r => r.AmountPaid),
                    receivable = rows.Sum(r => r.Debt),
                    vatTotal = rows.Sum(r => r.Vat),
                },
                // «Tekshirish kerak» — buxgalterning ish ro'yxati
                checks = new
                {
                    withoutInn = noInn,
                    overduePayments = overdue,
                    // Faqat raqam yetarli emas edi: ro'yxatdagi 13 qator
                    // bir xil ko'rinardi va bugalter kimga tegishli ekanini
                    // bilish uchun har birini ochib ko'rishga majbur edi.
                    withoutEsf = rows.Where(r => r.Status == "Active" && !r.HasEsf)
                                     .Select(r => new { r.Number, r.Counterparty, r.AmountTotal })
                                     .Take(20).ToList(),
                },
                deals = rows,
            });
        });

        // ══════════════════════════════════════════════════════════
        //  PEREHOD — hujjat harakati buxgalteri
        // ══════════════════════════════════════════════════════════
        g.MapGet("/perehod", async (AppDbContext db, CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (actor.Role is not (UserRole.Perehod or UserRole.Admin))
                throw new BusinessException("FORBIDDEN", "Bu bo'lim perehod buxgalteri uchun");

            var today = UzTime.Today;

            // Barcha ochiq bitimlarning hujjatlari — ikkala yo'nalish ham.
            var docs = await db.Documents.AsNoTracking()
                .Where(x => x.DealId != null
                         && x.Deal!.Status == DealStatus.Active
                         && x.Status != DocStatus.Cancelled && x.Status != DocStatus.Rejected)
                .Select(x => new
                {
                    DealId = x.DealId!.Value,
                    x.Deal!.Number,
                    x.Deal.Kind,
                    Counterparty = x.Deal.Counterparty!.Name,
                    x.Id,
                    x.DocType,
                    x.DocNumber,
                    x.DocDate,
                    x.Direction,
                    x.Amount,
                    x.ParentDocumentId,
                })
                .ToListAsync(ct);

            var chains = new List<object>();
            var brokenCount = 0;

            foreach (var g2 in docs.GroupBy(d => d.DealId))
            {
                var mine = g2.OrderBy(x => DealWorkflow.Chain.ToList().IndexOf(x.DocType)).ToList();
                var byId = mine.ToDictionary(x => x.Id);
                var breaks = DealWorkflow.CheckChain(
                    mine.Select(x => (x.DocType, x.Id, x.ParentDocumentId)));

                if (breaks.Count > 0) brokenCount++;

                var first = mine[0];
                chains.Add(new
                {
                    dealId = g2.Key,
                    number = first.Number,
                    kind = first.Kind.ToString(),
                    counterparty = first.Counterparty,
                    isBroken = breaks.Count > 0,
                    problems = breaks.Select(b => b.Problem).ToList(),
                    documents = mine.Select(x => new
                    {
                        x.Id,
                        type = x.DocType.ToString(),
                        label = DealWorkflow.DocLabel(x.DocType),
                        number = x.DocNumber,
                        date = x.DocDate,
                        direction = x.Direction.ToString(),
                        x.Amount,
                        basis = x.ParentDocumentId is { } p && byId.TryGetValue(p, out var pd)
                            ? DealWorkflow.DocLabel(pd.DocType) + " №" + pd.DocNumber
                            : null,
                        // Summa mosligi — perehod buxgalteri shuni tekshiradi
                        amountMatchesParent = x.ParentDocumentId is { } p2
                            && byId.TryGetValue(p2, out var pd2)
                            && Math.Abs(pd2.Amount - x.Amount) <= 1000m,
                    }).ToList(),
                });
            }

            // ── Kutilayotgan qadamlar: nima qayerdan qayerga o'tishi kerak ──
            var waiting = await db.DealSteps.AsNoTracking()
                .Where(s => s.Status == StepStatus.Pending
                         && s.Deal.Status == DealStatus.Active
                         && s.DueDate != null)
                .Select(s => new
                {
                    s.DealId,
                    s.Deal.Number,
                    s.Deal.Kind,
                    Counterparty = s.Deal.Counterparty!.Name,
                    s.Step,
                    s.DueDate,
                    s.Deal.ContractByUs,
                    s.Deal.TtnByUs,
                })
                .ToListAsync(ct);

            var waitingRows = waiting
                .OrderBy(w => w.DueDate)
                .Take(50)
                .Select(w => new
                {
                    w.DealId,
                    w.Number,
                    w.Counterparty,
                    step = w.Step.ToString(),
                    label = DealWorkflow.StepLabel(w.Step),
                    weAct = DealWorkflow.WeAct(w.Kind, w.Step, w.ContractByUs, w.TtnByUs),
                    text = DealWorkflow.StepWaitingText(w.Kind, w.Step, w.ContractByUs, w.TtnByUs),
                    dueDate = w.DueDate,
                    lateDays = w.DueDate! < today ? today.DayNumber - w.DueDate!.Value.DayNumber : 0,
                })
                .ToList();

            // ── Bitimga ilinmagan kiruvchi hujjatlar ──
            var unlinked = await db.Documents.AsNoTracking()
                .Where(x => x.DealId == null)
                .Select(x => new
                {
                    x.Id,
                    label = DealWorkflow.DocLabel(x.DocType),
                    x.DocNumber,
                    x.DocDate,
                    x.Amount,
                })
                .Take(20).ToListAsync(ct);

            return Results.Ok(new
            {
                role = "Perehod",
                summary = new
                {
                    activeChains = chains.Count,
                    brokenChains = brokenCount,
                    waitingSteps = waitingRows.Count,
                    lateSteps = waitingRows.Count(w => w.lateDays > 0),
                    unlinkedDocs = unlinked.Count,
                },
                chains,
                waiting = waitingRows,
                unlinked,
            });
        });

        // ══════════════════════════════════════════════════════════
        //  30 KUNLIK TO'LIQ HISOBOT — hamma rol ko'radi
        //  Har rol o'z kesimini oladi, admin ikkalasini ham.
        // ══════════════════════════════════════════════════════════
        g.MapGet("/hisobot", async (int? days, AppDbContext db, CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            var window = Math.Clamp(days ?? 30, 1, 365);

            var to = UzTime.Today;
            var from = to.AddDays(-window);
            var (fromTs, toTs) = UzTime.DayRangeUtc(from, to);

            // Rol kesimi: realizatsiya faqat sotuvni, perehod ikkalasini ko'radi
            // (chunki hujjat harakati ikkala yo'nalishda ham bo'ladi).
            DealKind? only = actor.Role == UserRole.Realizatsiya ? DealKind.Sale : null;

            var deals = db.Deals.AsNoTracking()
                .Where(d => only == null || d.Kind == only);

            // ── Davr ichida ochilgan va yopilganlar ──
            var opened = await deals.CountAsync(d => d.CreatedAt >= fromTs && d.CreatedAt < toTs, ct);
            var closed = await deals.CountAsync(d => d.ClosedAt >= fromTs && d.ClosedAt < toTs, ct);

            // ── Hujjat harakati: qaysi turdan nechta kelgan / ketgan ──
            var docs = await db.Documents.AsNoTracking()
                .Where(x => x.CreatedAt >= fromTs && x.CreatedAt < toTs
                         && x.Status != DocStatus.Cancelled)
                .GroupBy(x => new { x.DocType, x.Direction })
                .Select(gr => new { gr.Key.DocType, gr.Key.Direction, Count = gr.Count() })
                .ToListAsync(ct);

            var docRows = DealWorkflow.Chain.Select(t => new
            {
                type = t.ToString(),
                label = DealWorkflow.DocLabel(t),
                sent = docs.Where(x => x.DocType == t && x.Direction == DocDirection.Out).Sum(x => x.Count),
                received = docs.Where(x => x.DocType == t && x.Direction == DocDirection.In).Sum(x => x.Count),
            }).ToList();

            // ── Pul harakati ──
            var pays = await db.Payments.AsNoTracking()
                .Where(p2 => p2.PayDate >= from && p2.PayDate <= to)
                .GroupBy(p2 => p2.Direction)
                .Select(gr => new { Dir = gr.Key, Total = gr.Sum(x => x.Amount), Count = gr.Count() })
                .ToListAsync(ct);

            var paidOut = pays.FirstOrDefault(x => x.Dir == PaymentDirection.Out);
            var paidIn  = pays.FirstOrDefault(x => x.Dir == PaymentDirection.In);

            // ── Kontragent kesimi: kim bilan qancha ishladik, qancha qarz ──
            var byCp = await deals
                .Where(d => d.Status == DealStatus.Active || (d.ClosedAt >= fromTs && d.ClosedAt < toTs))
                .GroupBy(d => new { d.CounterpartyId, d.Counterparty.Name, d.Kind })
                .Select(gr => new
                {
                    name = gr.Key.Name,
                    kind = gr.Key.Kind.ToString(),
                    deals = gr.Count(),
                    total = gr.Sum(x => x.AmountTotal),
                    paid = gr.Sum(x => x.AmountPaid),
                    debt = gr.Sum(x => x.AmountTotal - x.AmountPaid),
                })
                .OrderByDescending(x => x.debt)
                .Take(50)
                .ToListAsync(ct);

            // ── Zanjir sog'ligi ──
            var chainRows = await db.Documents.AsNoTracking()
                .Where(x => x.DealId != null && x.Deal!.Status == DealStatus.Active
                         && x.Status != DocStatus.Cancelled && x.Status != DocStatus.Rejected)
                .Select(x => new { DealId = x.DealId!.Value, x.Id, x.DocType, x.ParentDocumentId })
                .ToListAsync(ct);

            var brokenDeals = chainRows.GroupBy(x => x.DealId)
                .Count(gr => DealWorkflow.CheckChain(
                    gr.Select(x => (x.DocType, x.Id, x.ParentDocumentId))).Count > 0);

            return Results.Ok(new
            {
                period = new { from, to, days = window },
                scope = only?.ToString() ?? "Hammasi",
                deals = new
                {
                    opened,
                    closed,
                    stillOpen = await deals.CountAsync(d => d.Status == DealStatus.Active, ct),
                },
                money = new
                {
                    paidOutTotal = paidOut?.Total ?? 0m,
                    paidOutCount = paidOut?.Count ?? 0,
                    receivedTotal = paidIn?.Total ?? 0m,
                    receivedCount = paidIn?.Count ?? 0,
                    weOwe = await deals.Where(d => d.Kind == DealKind.Purchase && d.Status == DealStatus.Active)
                                       .SumAsync(d => (decimal?)(d.AmountTotal - d.AmountPaid), ct) ?? 0m,
                    theyOwe = await deals.Where(d => d.Kind == DealKind.Sale && d.Status == DealStatus.Active)
                                         .SumAsync(d => (decimal?)(d.AmountTotal - d.AmountPaid), ct) ?? 0m,
                },
                documents = docRows,
                chainHealth = new { activeDeals = chainRows.Select(x => x.DealId).Distinct().Count(), broken = brokenDeals },
                counterparties = byCp,
            });
        });

        // ══════════════════════════════════════════════════════════
        //  ADMIN — umumiy manzara, ikkala tomon
        // ══════════════════════════════════════════════════════════
        g.MapGet("/admin", async (AppDbContext db, CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (actor.Role != UserRole.Admin)
                throw new BusinessException("FORBIDDEN", "Faqat admin");

            var today = UzTime.Today;
            var deals = db.Deals.AsNoTracking();

            var byKind = await deals
                .Where(d => d.Status == DealStatus.Active)
                .GroupBy(d => d.Kind)
                .Select(g2 => new
                {
                    kind = g2.Key.ToString(),
                    count = g2.Count(),
                    total = g2.Sum(x => x.AmountTotal),
                    paid = g2.Sum(x => x.AmountPaid),
                    debt = g2.Sum(x => x.AmountTotal - x.AmountPaid),
                })
                .ToListAsync(ct);

            var users = await db.Users.AsNoTracking()
                .Where(u => u.IsActive)
                .Select(u => new { u.Id, u.Username, u.FullName, Role = u.Role.ToString() })
                .ToListAsync(ct);

            var recent = await deals
                .OrderByDescending(d => d.CreatedAt)
                .Take(30)
                .Select(d => new
                {
                    d.Id, d.Number, kind = d.Kind.ToString(),
                    counterparty = d.Counterparty.Name,
                    d.AmountTotal, d.AmountPaid,
                    debt = d.AmountTotal - d.AmountPaid,
                    status = d.Status.ToString(),
                    d.CreatedAt,
                    stepsDone = d.Steps.Count(s => s.Status == StepStatus.Done),
                    stepsTotal = d.Steps.Count,
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                role = "Admin",
                byKind,
                users,
                recent,
                counts = new
                {
                    counterparties = await db.Counterparties.CountAsync(ct),
                    documents = await db.Documents.CountAsync(ct),
                    openDeals = await deals.CountAsync(d => d.Status == DealStatus.Active, ct),
                    closedDeals = await deals.CountAsync(d => d.Status == DealStatus.Closed, ct),
                },
            });
        });
    }
}

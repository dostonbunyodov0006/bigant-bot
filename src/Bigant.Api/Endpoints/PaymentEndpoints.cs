using Bigant.Api.Auth;
using Bigant.Domain.Enums;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/payments").WithTags("To'lovlar").RequireAuthorization().RequireRateLimiting("api");

        g.MapGet("", async (long? counterpartyId, long? dealId, DateOnly? from, DateOnly? to,
                            AppDbContext db, CancellationToken ct) =>
        {
            var q = db.Payments.AsNoTracking();
            if (counterpartyId is { } cid) q = q.Where(p => p.CounterpartyId == cid);
            if (dealId is { } did) q = q.Where(p => p.DealId == did);
            if (from is { } f) q = q.Where(p => p.PayDate >= f);
            if (to is { } t) q = q.Where(p => p.PayDate <= t);

            return Results.Ok(await q.OrderByDescending(p => p.PayDate).Take(300)
                .Select(p => new
                {
                    p.Id, counterparty = p.Counterparty.Name,
                    deal = p.Deal != null ? p.Deal.Number : null, p.DealId,
                    direction = p.Direction.ToString(),
                    p.Amount, p.PayDate, p.BankDocNo, p.Purpose, p.MatchedBy
                }).ToListAsync(ct));
        });

        g.MapPost("", async (NewPayment req, IPaymentService svc, CurrentUser cu, CancellationToken ct) =>
        {
            var p = await svc.RegisterAsync(req, await cu.GetAsync(ct), ct);
            return Results.Ok(new { p.Id, p.DealId, p.MatchedBy });
        });

        // ── BANK VIPISKASI IMPORTI ──────────────────────────────────
        // Bir necha qatorni birdan yuboring — tizim shartnomaga o'zi bog'laydi
        // (to'lov maqsadidagi shartnoma raqami → aynan mos summa → FIFO).
        g.MapPost("/import", async (List<NewPayment> rows, IPaymentService svc,
                                    CurrentUser cu, CancellationToken ct) =>
        {
            var (imported, matched) = await svc.ImportAsync(rows, await cu.GetAsync(ct), ct);
            return Results.Ok(new
            {
                imported, matched, unmatched = imported - matched, skipped = rows.Count - imported
            });
        });

        // Bog'lanmagan to'lovlar — bugalter qo'lda biriktiradi.
        g.MapGet("/unmatched", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Payments.AsNoTracking()
                .Where(p => p.DealId == null)
                .OrderByDescending(p => p.PayDate)
                .Take(200)
                .Select(p => new
                {
                    p.Id, counterparty = p.Counterparty.Name,
                    p.Amount, p.PayDate, p.BankDocNo, p.Purpose
                }).ToListAsync(ct)));

        // ── QARZDORLIK HISOBOTI ─────────────────────────────────────
        g.MapGet("/debts", async (bool? receivable, AppDbContext db, CancellationToken ct) =>
        {
            var kind = receivable == false ? DealKind.Purchase : DealKind.Sale;

            var rows = await db.Deals.AsNoTracking()
                .Where(d => d.Status == DealStatus.Active
                         && d.Kind == kind
                         && d.AmountPaid < d.AmountTotal)
                .GroupBy(d => new { d.CounterpartyId, d.Counterparty.Name })
                .Select(gr => new
                {
                    counterpartyId = gr.Key.CounterpartyId,
                    counterparty = gr.Key.Name,
                    deals = gr.Count(),
                    total = gr.Sum(d => d.AmountTotal - d.AmountPaid),
                    oldest = gr.Min(d => d.CreatedAt)
                })
                .OrderByDescending(x => x.total)
                .ToListAsync(ct);

            return Results.Ok(rows.Select(r => new
            {
                r.counterpartyId, r.counterparty, r.deals, r.total,
                ageDays = (int)(DateTimeOffset.UtcNow - r.oldest).TotalDays
            }));
        });
    }
}

using Bigant.Api.Auth;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

/// <summary>
/// USD → UZS kursi. Prays-listlar у.е. da kelgani uchun kerak.
///
/// Muhim: yangi kurs qo'shish MAVJUD shartnomalarni qayta hisoblamaydi.
/// Har bir shartnoma o'z kursini muzlatib qo'ygan (DealItem.RateSnapshot) —
/// aks holda kecha chiqarilgan faktura bugun boshqa summa ko'rsatardi.
/// </summary>
public static class RateEndpoints
{
    public sealed record SetRate(decimal Rate, string? Note);

    public static void MapRateEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/exchange-rate").WithTags("Kurs").RequireAuthorization();

        /// Joriy kurs — eng oxirgi yozuv.
        g.MapGet("", async (AppDbContext db, CancellationToken ct) =>
        {
            var cur = await db.ExchangeRates.AsNoTracking()
                .OrderByDescending(r => r.EffectiveFrom)
                .Select(r => new { r.Id, r.Rate, r.EffectiveFrom, r.Note })
                .FirstOrDefaultAsync(ct);

            var usdProducts = await db.Products.CountAsync(p => p.IsActive && p.Currency == "USD", ct);

            return Results.Ok(new
            {
                current = cur,
                usdProducts,
                needsRate = usdProducts > 0 && cur is null,
                hint = "Kurs o'zgarsa у.е. dagi barcha tovar narxi avtomatik qayta hisoblanadi. " +
                       "Chiqarilgan shartnomalar o'zgarmaydi."
            });
        });

        /// Kurs tarixi — kim, qachon, qanchaga o'zgartirgan.
        g.MapGet("/history", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ExchangeRates.AsNoTracking()
                .OrderByDescending(r => r.EffectiveFrom)
                .Take(50)
                .Select(r => new { r.Id, r.Rate, r.EffectiveFrom, r.Note, r.CreatedAt })
                .ToListAsync(ct)));

        g.MapPost("", async (SetRate req, CurrentUser cu, AppDbContext db,
                             IAuditService audit, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (actor.Role is not (UserRole.Admin or UserRole.Manager))
                throw new BusinessException("FORBIDDEN", "Kursni faqat rahbar o'zgartiradi");

            if (req.Rate <= 0)
                throw new BusinessException("BAD_RATE", "Kurs musbat bo'lishi kerak");
            if (req.Rate is < 1000 or > 100000)
                throw new BusinessException("BAD_RATE",
                    $"Kurs {req.Rate:N0} — bu haqiqatga o'xshamaydi. Tekshiring");

            var prev = await db.ExchangeRates.AsNoTracking()
                .OrderByDescending(r => r.EffectiveFrom)
                .Select(r => (decimal?)r.Rate).FirstOrDefaultAsync(ct);

            var row = new ExchangeRate
            {
                Rate = req.Rate,
                Note = req.Note,
                CreatedByUserId = actor.Id
            };
            db.ExchangeRates.Add(row);
            audit.Write(actor.Id, nameof(ExchangeRate), null, "set",
                new { old = prev }, new { rate = req.Rate });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                row.Id, row.Rate, previous = prev,
                message = prev is null
                    ? $"Kurs o'rnatildi: 1 у.е. = {req.Rate:N0} so'm"
                    : $"Kurs {prev:N0} → {req.Rate:N0} so'm. Yangi shartnomalarga qo'llanadi."
            });
        });
    }
}

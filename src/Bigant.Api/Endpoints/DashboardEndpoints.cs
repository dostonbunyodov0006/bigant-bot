using Bigant.Domain;
using Bigant.Didox;
using Bigant.Api.Auth;
using Bigant.Domain.Enums;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/dashboard").WithTags("Dashboard").RequireAuthorization().RequireRateLimiting("api").RahbarUchun();

        // ── "Nechta yopildi, nechta ochiq" ──────────────────────────
        g.MapGet("/summary", async (DateOnly? from, DateOnly? to, bool? mine,
                                    IDashboardService dash, IDidoxClient didox,
                                    CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            long? scope = mine == true ? actor.Id : null;

            var today = UzTime.Today;
            var f = from ?? today.AddDays(-30);
            var t = to ?? today;

            var s = await dash.SummaryAsync(f, t, scope, ct);
            // Didox holati sidebarda jonli ko'rinsin — qo'lda yozilgan matn emas.
            return Results.Ok(new
            {
                s.From, s.To, s.NewDeals, s.ClosedDeals, s.OpenDeals, s.OpenAmount,
                s.ReceivableTotal, s.PayableTotal, s.PaidNotShipped, s.ShippedNotPaid,
                s.OverdueSteps, s.UnreadNotifications,
                didoxEnabled = didox.IsEnabled
            });
        });

        // ── Ochiq shartnomalar + qizil signallar ────────────────────
        g.MapGet("/open-deals", async (bool? mine, int? take, IDashboardService dash,
                                       CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            long? scope = mine == true ? actor.Id : null;
            return Results.Ok(await dash.OpenDealsAsync(scope, take ?? 100, ct));
        });
        // ── Katalog sog'lig'i: MXIK'siz tovarlar Didox'ga to'siq ────
    }
}

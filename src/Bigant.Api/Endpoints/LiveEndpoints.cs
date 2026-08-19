using System.Text.Json;
using Bigant.Api.Auth;
using Bigant.Domain.Enums;
using Bigant.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

/// <summary>
/// JONLI OQIM (Server-Sent Events).
///
/// Dashboard har 15 soniyada so'rov yuborib turardi — bu ham sekin, ham
/// bekorga yuklama. Endi server yangi xabar paydo bo'lishi bilan O'ZI
/// bildiradi: bugalter botga «5 mln o'tkazdik» deb yozsa, dashboardda
/// bir soniya ichida ko'rinadi.
///
/// SSE tanlandi (WebSocket emas): bir tomonlama oqim yetarli, oddiy HTTP
/// ustida ishlaydi, proksi va launchd bilan muammo chiqarmaydi.
/// </summary>
public static class LiveEndpoints
{
    /// <summary>
    /// Maydon nomlari kichik harf bilan — API ning qolgan qismi bilan bir xil,
    /// aks holda frontda `n.Title` va `n.title` chalkashib ketardi.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Bazani qanchalik tez-tez tekshirish.
    ///
    /// Chorak soniya: xabar ekranda deyarli bir zumda paydo bo'ladi, lekin
    /// bazaga yuklama sezilmaydi — bu so'rov faqat `id > lastId` ni ko'radi
    /// va indeks bo'yicha ketadi. Uch xodim ulangan bo'lsa sekundiga 12 ta
    /// yengil so'rov, bu Postgres uchun hech narsa emas.
    /// </summary>
    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(250);

    /// <summary>Ulanish umri. Undan keyin brauzer o'zi qayta ulanadi.</summary>
    private static readonly TimeSpan MaxLife = TimeSpan.FromMinutes(30);

    public static void MapLiveEndpoints(this WebApplication app)
    {
        app.MapGet("/api/live", async (HttpContext ctx, AppDbContext db, CurrentUser cu,
                                       CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            var role = actor.Role;
            var seesAll = role == UserRole.Admin;

            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";   // proksi buferlamasin

            // Oxirgi ko'rilgan xabar. Shundan keyingilari yangi hisoblanadi.
            var lastId = await db.Notifications.AsNoTracking()
                .Where(n => seesAll || n.TargetRole == null || n.TargetRole == role)
                .OrderByDescending(n => n.Id)
                .Select(n => (long?)n.Id)
                .FirstOrDefaultAsync(ct) ?? 0;

            var started = DateTimeOffset.UtcNow;

            await Write(ctx, "ulandi", new { lastId }, ct);

            while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow - started < MaxLife)
            {
                try
                {
                    await Task.Delay(Beat, ct);
                }
                catch (OperationCanceledException) { break; }

                var fresh = await db.Notifications.AsNoTracking()
                    .Where(n => n.Id > lastId
                             && (seesAll || n.TargetRole == null || n.TargetRole == role))
                    .OrderBy(n => n.Id)
                    .Take(20)
                    .Select(n => new
                    {
                        n.Id,
                        kind = n.Kind.ToString(),
                        severity = n.Severity.ToString(),
                        n.Title,
                        n.Body,
                        n.DealId,
                        n.CreatedAt,
                    })
                    .ToListAsync(ct);

                if (fresh.Count > 0)
                {
                    lastId = fresh[^1].Id;
                    await Write(ctx, "xabar", fresh, ct);
                }
                else
                {
                    // Tirikligini bildiruvchi belgi — proksi ulanishni uzmasin.
                    await ctx.Response.WriteAsync(": beat\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }

            return Results.Empty;
        }).RequireAuthorization();
    }

    private static async Task Write(HttpContext ctx, string evt, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, Json);
        await ctx.Response.WriteAsync($"event: {evt}\ndata: {json}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
}

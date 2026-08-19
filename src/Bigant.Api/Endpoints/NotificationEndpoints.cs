using Bigant.Api.Auth;
using Bigant.Domain.Enums;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Jobs;
using Bigant.Infrastructure.Notifications;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigant.Api.Endpoints;

public static class NotificationEndpoints
{
    /// <summary>
    /// `Ids` bo'sh kelishi mumkin — brauzer uzilib qolgan so'rov yuborsa
    /// yoki tashqi vosita chaqirsa. Ilgari bunday holatda server ichki
    /// xato (500) qaytarardi.
    /// </summary>
    public sealed record MarkReadRequest(List<long>? Ids);

    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/notifications").WithTags("Eslatmalar").RequireAuthorization().RequireRateLimiting("api");

        g.MapGet("", async (bool? unreadOnly, int? take, int? skip, CurrentUser cu,
                            AppDbContext db, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);

            // HAR ROL FAQAT O'Z XABARINI ko'radi. Admin — hammasini.
            // TargetRole null bo'lsa xabar ikkala bugalterga ham tegishli.
            var role = actor.Role;
            var seesAll = role == UserRole.Admin;

            var q = db.Notifications.AsNoTracking()
                .Where(n => seesAll || n.TargetRole == null || n.TargetRole == role);
            if (unreadOnly == true) q = q.Where(n => !n.IsRead);

            // `Id` bo'yicha ham tartiblaymiz: bir soniyada tug'ilgan bir nechta
            // eslatma sahifalar orasida joyini almashtirib, biri ikki marta,
            // boshqasi umuman ko'rinmay qolmasligi uchun.
            var rows = await q.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, 200))
                .Select(n => new
                {
                    n.Id, kind = n.Kind.ToString(), severity = n.Severity.ToString(),
                    n.Title, n.Body, n.DealId, n.DocumentId, n.IsRead, n.CreatedAt,
                    // Qaysi rol uchun ekani ko'rinsin: admin hammasini ko'radi va
                    // qaysi biri kimga tegishli ekanini ajrata olishi kerak.
                    targetRole = n.TargetRole == null ? null : n.TargetRole.ToString()
                }).ToListAsync(ct);

            var unread = await db.Notifications
                .CountAsync(n => !n.IsRead && (seesAll || n.TargetRole == null || n.TargetRole == role), ct);

            return Results.Ok(new { unread, items = rows });
        });

        g.MapPost("/read", async (MarkReadRequest body, CurrentUser cu,
                                  INotificationService svc, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (body.Ids is not { Count: > 0 }) return Results.Ok(new { marked = 0 });

            var seesAll = true;
            var n = await svc.MarkReadAsync(actor.Id, body.Ids, seesAll, ct);
            return Results.Ok(new { marked = n });
        });

        // «Hammasini o'qildi» — ekranga yuklanganini emas, HAMMASINI.
        // Ilgari tugma faqat ochilgan ro'yxatdagi id larni yuborardi, shuning
        // uchun mingta eslatma bo'lsa hisoblagich nolga tushmasdi.
        g.MapPost("/read-all", async (CurrentUser cu, AppDbContext db, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            var seesAll = actor.Role == UserRole.Admin;

            var n = await db.Notifications
                .Where(x => !x.IsRead
                         && (seesAll || x.TargetRole == null || x.TargetRole == actor.Role))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.IsRead, true)
                    .SetProperty(x => x.ReadAt, DateTimeOffset.UtcNow), ct);

            return Results.Ok(new { marked = n });
        });

        // ── TELEGRAM ──
        // Kod yo'q: botga bir marta /start yoziladi, bot chatni o'zi eslab qoladi.
        g.MapGet("/telegram", async (AppDbContext db, IOptions<TelegramOptions> tg,
                                     CancellationToken ct) =>
        {
            var chat = await db.CompanyProfiles.Select(c => c.TelegramChatId).FirstOrDefaultAsync(ct);
            var bot = tg.Value.BotUsername?.TrimStart('@');
            return Results.Ok(new
            {
                enabled = tg.Value.Enabled,
                connected = !string.IsNullOrWhiteSpace(chat),
                botUsername = bot,
                link = bot is null ? null : $"https://t.me/{bot}",
                hint = "Botni oching va bir marta /start yozing. Boshqa hech narsa kerak emas."
            });
        });

        g.MapDelete("/telegram", async (CurrentUser cu, AppDbContext db, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (actor.Role != UserRole.Admin)
                throw new BusinessException("FORBIDDEN", "Faqat rahbar uzadi");

            var p = await db.CompanyProfiles.FirstOrDefaultAsync(ct);
            if (p is not null) { p.TelegramChatId = null; await db.SaveChangesAsync(ct); }
            return Results.Ok(new { disconnected = true });
        });

        // Eslatma motorini qo'lda ishga tushirish (tekshirish uchun).
        g.MapPost("/run-check", async (CurrentUser cu, ReminderJob job, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (actor.Role != UserRole.Admin)
                throw new BusinessException("FORBIDDEN", "Ruxsat yo'q");

            await job.RunAsync(ct);
            return Results.Ok(new { ok = true });
        });
    }
}

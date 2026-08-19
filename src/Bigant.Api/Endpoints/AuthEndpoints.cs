using Bigant.Infrastructure.Notifications;
using Microsoft.Extensions.Options;
using Bigant.Api.Auth;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record TelegramLoginRequest(string InitData);

    public sealed record LoginRequest(string Username, string Password);
    public sealed record CreateUserRequest(string Username, string Password, string FullName,
        UserRole Role, string? TelegramChatId,
        /// <summary>Xodimning Telegram @username'i. Shu yozilsa, xodim botga
        /// /start bosishi bilan o'zi ulanadi — kod yoki tugma kerak emas.</summary>
        string? TelegramUsername);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");

        g.MapPost("/login", async (LoginRequest req, AppDbContext db, JwtService jwt, CancellationToken ct) =>
        {
            const int MaxFailed = 5;
            var lockFor = TimeSpan.FromMinutes(15);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username && u.IsActive, ct);

            // ── HISOB BLOKLANGANMI ──
            // IP chegarasi yolg'iz yetarli emas: hujumchi manzil almashtirishi mumkin.
            if (user is { LockedUntil: { } until } && until > DateTimeOffset.UtcNow)
            {
                var left = (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalMinutes);
                return Results.Json(
                    new { code = "LOCKED", message = $"Hisob vaqtincha bloklangan. {left} daqiqadan keyin urining." },
                    statusCode: StatusCodes.Status423Locked);
            }

            // Vaqt bo'yicha hujum qilinmasligi uchun har doim hash tekshiriladi.
            var ok = user is not null && BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash);

            if (!ok)
            {
                if (user is not null)
                {
                    user.FailedLogins++;
                    if (user.FailedLogins >= MaxFailed)
                    {
                        user.LockedUntil = DateTimeOffset.UtcNow.Add(lockFor);
                        user.FailedLogins = 0;
                    }
                    await db.SaveChangesAsync(ct);
                }

                // Javob har doim bir xil — hisob bor-yo'qligi bilinmasin.
                return Results.Json(new { code = "BAD_CREDENTIALS", message = "Login yoki parol noto'g'ri" },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            // Muvaffaqiyatli kirish — hisoblagich nolga tushadi.
            if (user!.FailedLogins > 0 || user.LockedUntil is not null)
            {
                user.FailedLogins = 0;
                user.LockedUntil = null;
                await db.SaveChangesAsync(ct);
            }

            var (token, expires) = jwt.Create(user);
            return Results.Ok(new
            {
                token,
                expiresAt = expires,
                user = new { user!.Id, user.Username, user.FullName, Role = user.Role.ToString() }
            });
        }).AllowAnonymous().RequireRateLimiting("login");

        // ══════════════════════════════════════════════════════════
        //  TELEGRAM MINI APP ORQALI KIRISH
        //
        //  Dashboard Telegram ichida ochilganda login-parol so'ralmaydi:
        //  Telegram imzolagan ma'lumot tekshiriladi va shu asosda token
        //  beriladi. Imzo bot tokeni bilan qo'yilgan — soxtalashtirib bo'lmaydi.
        // ══════════════════════════════════════════════════════════
        g.MapPost("/telegram", async (TelegramLoginRequest req, AppDbContext db, JwtService jwt,
                                      IOptions<TelegramOptions> tg, ILoggerFactory lf,
                                      CancellationToken ct) =>
        {
            var log = lf.CreateLogger("TelegramAuth");
            var v = TelegramAuth.Verify(req.InitData, tg.Value.BotToken ?? "");

            if (!v.Ok)
            {
                log.LogWarning("Telegram kirish rad etildi: {Err} ({Data})",
                    v.Error, TelegramAuth.Short(req.InitData ?? ""));
                return Results.Json(new { code = "BAD_INIT_DATA", message = "Telegram ma'lumoti tasdiqlanmadi" },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var chatId = v.TelegramId.ToString();

            // 1) Telegram akkaunti biriktirilgan xodim
            var user = await db.Users.FirstOrDefaultAsync(
                u => u.TelegramChatId == chatId && u.IsActive, ct);

            // 2) Bo'lmasa — kompaniya kanalini egallagan odam (egasi) admin sifatida
            if (user is null)
            {
                var companyChat = await db.CompanyProfiles
                    .Select(c => c.TelegramChatId).FirstOrDefaultAsync(ct);

                if (companyChat == chatId)
                    user = await db.Users.FirstOrDefaultAsync(
                        u => u.Role == UserRole.Admin && u.IsActive, ct);
            }

            if (user is null)
            {
                log.LogWarning("Telegram kirish: {Id} ({Name}) tizimda yo'q", v.TelegramId, v.Name);
                return Results.Json(
                    new { code = "NOT_LINKED", message = "Telegram akkauntingiz tizimga biriktirilmagan. Administratorga murojaat qiling." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var (tok, exp) = jwt.Create(user);
            log.LogInformation("Telegram orqali kirdi: {User}", user.Username);

            return Results.Ok(new
            {
                token = tok,
                expiresAt = exp,
                user = new { user.Id, user.Username, user.FullName, Role = user.Role.ToString() }
            });
        }).AllowAnonymous().RequireRateLimiting("login");

        g.MapGet("/me", async (CurrentUser cu, CancellationToken ct) =>
        {
            var u = await cu.GetAsync(ct);
            return Results.Ok(new
            {
                u.Id, u.Username, u.FullName, Role = u.Role.ToString(),
                hasTelegram = !string.IsNullOrWhiteSpace(u.TelegramChatId)
            });
        }).RequireAuthorization();

        g.MapPost("/users", async (CreateUserRequest req, CurrentUser cu, AppDbContext db,
                                   IAuditService audit, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (actor.Role != UserRole.Admin)
                throw new BusinessException("FORBIDDEN", "Foydalanuvchi qo'shishga huquqingiz yo'q");

            // Maydonlar umuman kelmasligi mumkin (bo'sh so'rov, tashqi vosita).
            // Ilgari bunday holatda server ichki xato (500) qaytarardi.
            if (string.IsNullOrWhiteSpace(req.Username))
                throw new BusinessException("NO_USERNAME", "Login kiritilmagan");
            if (string.IsNullOrWhiteSpace(req.FullName))
                throw new BusinessException("NO_NAME", "F.I.Sh kiritilmagan");
            if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
                throw new BusinessException("WEAK_PASSWORD", "Parol kamida 8 belgi bo'lsin");
            if (!Enum.IsDefined(req.Role))
                throw new BusinessException("BAD_ROLE", "Rol noto'g'ri ko'rsatilgan");

            if (await db.Users.AnyAsync(u => u.Username == req.Username, ct))
                throw new BusinessException("DUPLICATE", "Bunday login allaqachon bor");

            var user = new AppUser
            {
                Username = req.Username,
                FullName = req.FullName,
                Role = req.Role,

                TelegramChatId = req.TelegramChatId,
                TelegramUsername = req.TelegramUsername?.TrimStart('@'),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password)
            };
            db.Users.Add(user);
            audit.Write(actor.Id, nameof(AppUser), null, "create",
                null, new { req.Username, Role = req.Role.ToString() });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { user.Id, user.Username });
        }).RequireAuthorization();

        g.MapGet("/users", async (CurrentUser cu, AppDbContext db, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (actor.Role != UserRole.Admin)
                throw new BusinessException("FORBIDDEN", "Ruxsat yo'q");

            return Results.Ok(await db.Users.AsNoTracking()
                .OrderBy(u => u.FullName)
                .Select(u => new
                {
                    u.Id, u.Username, u.FullName, Role = u.Role.ToString(),
                    u.IsActive,
                    hasTelegram = u.TelegramChatId != null
                }).ToListAsync(ct));
        }).RequireAuthorization();
    }
}

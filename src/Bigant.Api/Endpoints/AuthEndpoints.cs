using Bigant.Api.Auth;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record LoginRequest(string Username, string Password);
    public sealed record CreateUserRequest(string Username, string Password, string FullName,
        UserRole Role, string? TelegramChatId);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");

        g.MapPost("/login", async (LoginRequest req, AppDbContext db, JwtService jwt, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username && u.IsActive, ct);

            // Vaqt bo'yicha hujum qilinmasligi uchun har doim hash tekshiriladi.
            var ok = user is not null && BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash);
            if (!ok)
                return Results.Json(new { code = "BAD_CREDENTIALS", message = "Login yoki parol noto'g'ri" },
                    statusCode: StatusCodes.Status401Unauthorized);

            var (token, expires) = jwt.Create(user!);
            return Results.Ok(new
            {
                token,
                expiresAt = expires,
                user = new { user!.Id, user.Username, user.FullName, Role = user.Role.ToString() }
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

            if (req.Password.Length < 8)
                throw new BusinessException("WEAK_PASSWORD", "Parol kamida 8 belgi bo'lsin");
            if (await db.Users.AnyAsync(u => u.Username == req.Username, ct))
                throw new BusinessException("DUPLICATE", "Bunday login allaqachon bor");

            var user = new AppUser
            {
                Username = req.Username,
                FullName = req.FullName,
                Role = req.Role,

                TelegramChatId = req.TelegramChatId,
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

using System.Security.Claims;
using Bigant.Domain.Entities;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Auth;

/// <summary>
/// Joriy foydalanuvchini bazadan oladi.
/// Rol va chegirma chegarasi tokendan emas, bazadan olinadi —
/// huquq o'zgarganda eski token bilan eskisi ishlamasin.
/// </summary>
public sealed class CurrentUser
{
    private readonly IHttpContextAccessor _ctx;
    private readonly AppDbContext _db;
    private AppUser? _cached;

    public CurrentUser(IHttpContextAccessor ctx, AppDbContext db)
    {
        _ctx = ctx; _db = db;
    }

    public async Task<AppUser> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var idText = _ctx.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idText, out var id))
            throw new BusinessException("UNAUTHORIZED", "Foydalanuvchi aniqlanmadi");

        _cached = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct)
                  ?? throw new BusinessException("UNAUTHORIZED", "Foydalanuvchi topilmadi yoki bloklangan");

        return _cached;
    }
}

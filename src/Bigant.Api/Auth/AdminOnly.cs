using Bigant.Domain.Enums;
using Bigant.Infrastructure.Services;

namespace Bigant.Api.Auth;

/// <summary>
/// BUTUN BO'LIMNI RAHBARGA CHEGARALASH.
///
/// Ba'zi endpointlar dashboardda ishlatilmaydi — ular eski tuzilishdan
/// qolgan. Lekin ular ochiq turgani uchun buxgalter manzilni qo'lda
/// yozib, o'z yo'nalishidan tashqaridagi ma'lumotni ko'ra olardi:
/// realizatsiya buxgalteri xarid to'lovlarini, perehod esa sotuvni.
///
/// Ekrandan olib tashlash yetarli emas — himoya serverda turishi kerak.
/// </summary>
public static class AdminOnly
{
    public static RouteGroupBuilder RahbarUchun(this RouteGroupBuilder g) =>
        (RouteGroupBuilder)g.AddEndpointFilter(async (ctx, next) =>
        {
            var cu = ctx.HttpContext.RequestServices.GetRequiredService<CurrentUser>();
            var actor = await cu.GetAsync(ctx.HttpContext.RequestAborted);
            if (actor.Role != UserRole.Admin)
                throw new BusinessException("FORBIDDEN", "Bu bo'lim rahbar uchun");
            return await next(ctx);
        });
}

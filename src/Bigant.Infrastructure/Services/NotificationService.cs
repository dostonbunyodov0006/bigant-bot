using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bigant.Infrastructure.Services;

/// <summary>Eslatmani yetkazish kanali (tizim ichi, Telegram, keyin email...).</summary>
public interface INotificationChannel
{
    string Name { get; }
    Task<bool> PushAsync(Notification n, AppUser? user, CancellationToken ct = default);
}

public interface INotificationService
{
    /// <summary>
    /// Eslatma qo'shadi. DedupKey bo'yicha takrorlanmaydi —
    /// bitta muammo bo'yicha kuniga 50 ta xabar kelmaydi.
    /// </summary>
    Task<bool> RaiseAsync(Notification n, CancellationToken ct = default);

    /// <param name="seesAll">Rahbar/bugalter — birovning eslatmasini ham o'qildi qila oladi.</param>
    Task<int> MarkReadAsync(long userId, IEnumerable<long> ids, bool seesAll = false,
                            CancellationToken ct = default);
    Task PushPendingAsync(CancellationToken ct = default);
}

public sealed class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<NotificationService> _log;

    public NotificationService(AppDbContext db,
                               IEnumerable<INotificationChannel> channels,
                               ILogger<NotificationService> log)
    {
        _db = db; _channels = channels; _log = log;
    }

    public async Task<bool> RaiseAsync(Notification n, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(n.DedupKey))
            throw new BusinessException("NO_DEDUP_KEY", "Eslatmada DedupKey bo'lishi shart");

        // ── KIMGA TEGISHLI: SHARTNOMA YO'NALISHI HAL QILADI ──
        //
        // Ilgari rol xabarning TURIga qarab qo'yilardi: masalan «pul keldi,
        // tovar berilmadi» har doim realizatsiyaga ketardi. Lekin bu xabar
        // XARID shartnomasida chiqsa, ma'nosi boshqacha — biz to'ladik,
        // tovar kelmadi — va bu perehodning ishi.
        //
        // Natijada realizatsiya buxgalteri xarid haqidagi xabarlarni,
        // perehod esa sotuv haqidagilarni olardi. Ekranlarni rolga bo'lib
        // qo'yib, xabarlarni bo'lmaslik — yarim ish.
        //
        // Shartnomaga bog'liq bo'lmagan xabarlar (tizim, umumiy xulosa)
        // o'z rolida qoladi.
        if (n.DealId is { } dealId)
        {
            var kind = await _db.Deals.AsNoTracking()
                .Where(d => d.Id == dealId)
                .Select(d => (DealKind?)d.Kind)
                .FirstOrDefaultAsync(ct);

            if (kind is { } k)
                n.TargetRole = k == DealKind.Sale ? UserRole.Realizatsiya : UserRole.Perehod;
        }

        // Dedup indeksga tayanamiz — poyga holatida ham ikkinchisi tushmaydi.
        if (await _db.Notifications.AnyAsync(x => x.DedupKey == n.DedupKey, ct))
            return false;

        _db.Notifications.Add(n);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            _db.Entry(n).State = EntityState.Detached;
            return false;   // parallel oqim allaqachon qo'shgan
        }
    }

    public async Task<int> MarkReadAsync(long userId, IEnumerable<long> ids, bool seesAll = false,
                                         CancellationToken ct = default)
    {
        var list = ids.ToArray();
        return await _db.Notifications
            .Where(x => list.Contains(x.Id) && (seesAll || x.UserId == null || x.UserId == userId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsRead, true)
                .SetProperty(x => x.ReadAt, DateTimeOffset.UtcNow), ct);
    }

    /// <summary>Hali tashqi kanalga yuborilmagan eslatmalarni jo'natadi.</summary>
    public async Task PushPendingAsync(CancellationToken ct = default)
    {
        var pending = await _db.Notifications
            .Where(x => !x.IsPushed)
            .OrderBy(x => x.Id)
            .Take(200)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        var userIds = pending.Where(p => p.UserId != null).Select(p => p.UserId!.Value).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        foreach (var n in pending)
        {
            var user = n.UserId is { } uid && users.TryGetValue(uid, out var u) ? u : null;
            var any = false;

            foreach (var ch in _channels)
            {
                try { any |= await ch.PushAsync(n, user, ct); }
                catch (Exception ex) { _log.LogWarning(ex, "{Channel} kanali xato berdi", ch.Name); }
            }

            n.IsPushed = true;
            _ = any;
        }

        await _db.SaveChangesAsync(ct);
    }

    // ── Eslatma qurish yordamchilari ────────────────────────────────
    public static Notification ForDeal(Deal deal, NotificationKind kind, NotificationSeverity sev,
                                       string title, string body, string dedupSuffix)
        => new()
        {
            Kind = kind,
            Severity = sev,
            UserId = deal.ManagerId,
            DealId = deal.Id,
            Title = title,
            Body = body,
            DedupKey = $"{kind}:deal:{deal.Id}:{dedupSuffix}"
        };
}

/// <summary>Tizim ichidagi kanal — eslatma allaqachon bazada, qo'shimcha ish yo'q.</summary>
public sealed class InAppChannel : INotificationChannel
{
    public string Name => "in-app";
    public Task<bool> PushAsync(Notification n, AppUser? user, CancellationToken ct = default)
        => Task.FromResult(true);
}

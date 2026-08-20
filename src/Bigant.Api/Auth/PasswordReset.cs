using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Bigant.Api.Auth;

/// <summary>
/// PAROLNI TIKLASH KODI.
///
/// Kod bazada saqlanmaydi — u atigi 10 daqiqa yashaydi va xotirada turadi.
/// Shu sabab server qayta ishga tushsa kod kuchini yo'qotadi: bu kamchilik
/// emas, xavfsizlik foydasiga qilingan tanlov. Xodim yangisini so'raydi.
///
/// Kodning o'zi emas, uning izi (hash) saqlanadi — xotirani ko'rgan odam ham
/// kodni bilib ololmaydi.
/// </summary>
public sealed class PasswordReset(IMemoryCache cache)
{
    /// <summary>Kod shuncha vaqt kuchda bo'ladi.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Shuncha marta xato kiritilsa kod bekor qilinadi.</summary>
    private const int MaxAttempts = 5;

    private sealed class Entry
    {
        public required byte[] Hash { get; init; }
        public int Attempts { get; set; }
    }

    private static string Key(long userId) => $"pwreset:{userId}";

    /// <summary>Yangi kod yaratadi. Eskisi bo'lsa — o'chadi.</summary>
    public string Issue(long userId)
    {
        // 6 xonali kod: xodim telefondan o'qib, klaviaturada teradi.
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        cache.Set(Key(userId), new Entry { Hash = Hash(code) },
                  new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime });
        return code;
    }

    /// <summary>Kod to'g'rimi. To'g'ri bo'lsa kod darhol kuchini yo'qotadi.</summary>
    public bool Verify(long userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        if (!cache.TryGetValue(Key(userId), out Entry? e) || e is null) return false;

        // Taqqoslash vaqti kodga bog'liq bo'lmasin.
        if (CryptographicOperations.FixedTimeEquals(e.Hash, Hash(code.Trim())))
        {
            cache.Remove(Key(userId));   // bir marta ishlatiladi
            return true;
        }

        if (++e.Attempts >= MaxAttempts) cache.Remove(Key(userId));
        return false;
    }

    private static byte[] Hash(string code) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("bigant-reset:" + code));
}

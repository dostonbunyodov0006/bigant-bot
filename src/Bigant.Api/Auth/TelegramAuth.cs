using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace Bigant.Api.Auth;

/// <summary>
/// TELEGRAM MINI APP AUTENTIFIKATSIYASI.
///
/// Dashboard Telegram ichida ochilganda Telegram unga `initData` beradi —
/// foydalanuvchi ma'lumoti va IMZO. Imzo bot tokeni bilan qo'yilgan,
/// ya'ni uni faqat biz tekshira olamiz. Tekshiruv o'tsa bugalter
/// login-parol kiritmaydi: Telegram uni allaqachon tanigan.
///
/// Rasmiy qoida (Telegram hujjati):
///   secret = HMAC_SHA256(key: "WebAppData", data: bot_token)
///   hash   = HMAC_SHA256(key: secret,       data: data_check_string)
/// `data_check_string` — `hash` dan boshqa hamma maydon, kalit bo'yicha
/// alifbo tartibida, `\n` bilan ulangan.
/// </summary>
public static class TelegramAuth
{
    /// <summary>
    /// Ma'lumot qancha vaqt amal qiladi. Eski `initData` ni qayta ishlatib
    /// bo'lmasligi uchun — o'g'irlangan havola abadiy ishlamasin.
    /// </summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    public sealed record Result(bool Ok, long TelegramId, string? Name, string? Error);

    public static Result Verify(string initData, string botToken)
    {
        if (string.IsNullOrWhiteSpace(initData)) return new(false, 0, null, "initData bo'sh");
        if (string.IsNullOrWhiteSpace(botToken)) return new(false, 0, null, "bot tokeni sozlanmagan");

        var q = HttpUtility.ParseQueryString(initData);
        var hash = q["hash"];
        if (string.IsNullOrWhiteSpace(hash)) return new(false, 0, null, "imzo yo'q");

        // ── data_check_string ──
        var pairs = q.AllKeys
            .Where(k => k is not null && k != "hash")
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => $"{k}={q[k]}");
        var check = string.Join("\n", pairs);

        // ── imzoni qayta hisoblaymiz ──
        var secret = HMACSHA256.HashData("WebAppData"u8.ToArray(), Encoding.UTF8.GetBytes(botToken));
        var mine = Convert.ToHexString(
            HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(check))).ToLowerInvariant();

        // Vaqt bo'yicha hujumga qarshi — uzunlikdan qat'i nazar bir xil vaqt.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(mine), Encoding.UTF8.GetBytes(hash.ToLowerInvariant())))
            return new(false, 0, null, "imzo mos kelmadi");

        // ── eskirmaganmi ──
        if (long.TryParse(q["auth_date"], out var unix))
        {
            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
            if (age > MaxAge) return new(false, 0, null, "ma'lumot eskirgan — qayta oching");
        }

        // ── foydalanuvchi ──
        var userJson = q["user"];
        if (string.IsNullOrWhiteSpace(userJson))
            return new(false, 0, null, "foydalanuvchi ma'lumoti yo'q");

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(userJson);
            var root = doc.RootElement;
            var id = root.GetProperty("id").GetInt64();

            var first = root.TryGetProperty("first_name", out var f) ? f.GetString() : null;
            var last = root.TryGetProperty("last_name", out var l) ? l.GetString() : null;
            var name = string.Join(' ', new[] { first, last }.Where(x => !string.IsNullOrWhiteSpace(x)));

            return new(true, id, string.IsNullOrWhiteSpace(name) ? null : name, null);
        }
        catch (Exception ex)
        {
            return new(false, 0, null, "foydalanuvchi ma'lumotini o'qib bo'lmadi: " + ex.Message);
        }
    }

    /// <summary>Log uchun — butun initData ni yozib qo'ymaymiz, imzo bor.</summary>
    public static string Short(string initData) =>
        initData.Length > 60 ? initData[..60] + "…" : initData;

}

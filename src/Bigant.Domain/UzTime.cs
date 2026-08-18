namespace Bigant.Domain;

/// <summary>
/// O'ZBEKISTON VAQTI (UTC+5) — yagona manba.
///
/// Qoida:
///   • Bazada barcha vaqt belgilari UTC da saqlanadi (timestamptz).
///   • "Bugun", "muddat", "kechikdi" kabi SANA hisoblari O'zbekiston
///     vaqti bo'yicha bo'ladi — aks holda kechqurun soat 05:00 dan keyin
///     UTC sanasi almashib, eslatma ikki marta chiqadi yoki bir kun kechikadi.
///
/// Kodda hech qachon to'g'ridan-to'g'ri DateOnly.FromDateTime(DateTime.UtcNow)
/// yozmang — UzTime.Today ishlating.
/// </summary>
public static class UzTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    /// <summary>Hozirgi Toshkent vaqti.</summary>
    public static DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone);

    /// <summary>Bugungi sana — Toshkent bo'yicha.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <summary>Eslatma dedup kaliti uchun barqaror kun belgisi.</summary>
    public static string DayStamp => Today.ToString("yyyy-MM-dd");

    /// <summary>UTC vaqtni Toshkent vaqtiga o'giradi (ko'rsatish uchun).</summary>
    public static DateTimeOffset ToUz(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    /// <summary>Toshkent sanasi va soatini UTC ga o'giradi (so'rov filtri uchun).</summary>
    public static DateTimeOffset FromUzDate(DateOnly d, TimeOnly? t = null)
    {
        var local = d.ToDateTime(t ?? TimeOnly.MinValue);
        var offset = Zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>Kun boshi va oxiri (UTC oralig'i) — hisobotlar uchun.</summary>
    public static (DateTimeOffset from, DateTimeOffset to) DayRangeUtc(DateOnly from, DateOnly to)
        => (FromUzDate(from), FromUzDate(to.AddDays(1)));

    private static TimeZoneInfo Resolve()
    {
        // Linux/Docker → IANA, Windows → o'z identifikatori.
        foreach (var id in new[] { "Asia/Tashkent", "West Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // Oxirgi chora — O'zbekistonda yozgi vaqt yo'q, qat'iy +5.
        return TimeZoneInfo.CreateCustomTimeZone("UZT", TimeSpan.FromHours(5), "Uzbekistan Time", "UZT");
    }
}

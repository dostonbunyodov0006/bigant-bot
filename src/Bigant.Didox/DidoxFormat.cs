using System.Globalization;

namespace Bigant.Didox;

/// <summary>
/// Didox raqam va sana formatlash qoidalari. Rasmiy hujjatdan:
///
///   • Count (miqdor) — 6 xonagacha
///   • Qolgan barcha raqamlar (Summa, DeliverySum, VatRate, VatSum,
///     ExciseRate, ExciseSum, DeliverySumWithVat, LgotaVatSum,
///     BaseSumma, ProfitRate) — 2 xonagacha
///   • Kasr ajratgich — NUQTA. Razryad ajratgich (bo'sh joy, vergul) TAQIQLANGAN
///   • Sana — faqat yyyy-MM-dd, vaqtsiz, zonasiz
///
/// Shuning uchun barcha raqamlar matn sifatida yuboriladi —
/// serializator lokalga qarab vergul qo'yib yubormasin.
/// </summary>
public static class DidoxFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Pul va stavkalar — 2 xona, nuqta bilan.</summary>
    public static string Sum(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", Inv);

    /// <summary>Miqdor — 6 xonagacha, ortiqcha nollarsiz.</summary>
    public static string Count(decimal value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero)
            .ToString("0.######", Inv);

    /// <summary>Sana — qat'iy yyyy-MM-dd.</summary>
    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
    public static string? Date(DateOnly? d) => d is null ? null : Date(d.Value);

    /// <summary>
    /// Didox bo'sh matnni ham qabul qiladi, lekin null obyektni afzal ko'radi.
    /// Bo'sh/probel matn null ga aylantiriladi.
    /// </summary>
    public static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Majburiy matn — bo'sh bo'lsa aniq xato beradi.</summary>
    public static string Required(string? s, string fieldName) =>
        string.IsNullOrWhiteSpace(s)
            ? throw new InvalidOperationException($"Didox: \"{fieldName}\" maydoni majburiy, lekin bo'sh")
            : s.Trim();
}

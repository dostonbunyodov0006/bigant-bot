namespace Bigant.Domain.Entities;

/// <summary>
/// USD → UZS kursi. Qo'lda yuritiladi, GLOBAL, tarix saqlanadi.
///
/// «Joriy kurs» = eng oxirgi yozuv. Yangi kurs qo'shish o'zi hech narsani
/// qayta hisoblamaydi — u faqat YANGI shartnomalarga ta'sir qiladi.
/// Chiqarilgan shartnomadagi summa MUZLATILGAN (DealItem.RateSnapshot):
/// kurs o'zgardi deb eski faktura o'zgarib ketmasligi kerak.
/// </summary>
public class ExchangeRate
{
    public long Id { get; set; }

    /// <summary>1 у.е. (USD) necha so'm.</summary>
    public decimal Rate { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; } = DateTimeOffset.UtcNow;
    public long? CreatedByUserId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

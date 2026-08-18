namespace Bigant.Domain.Entities;

/// <summary>
/// BIGANT'NING O'Z REKVIZITLARI — bitta yozuv (singleton).
/// Didox har bir hujjatda Seller/Buyer/Owner sifatida shularni talab qiladi.
/// Director maydoni MAJBURIY — bo'sh bo'lsa Didox hujjatni rad etadi.
/// </summary>
public class CompanyProfile
{
    public long Id { get; set; }

    public string Name { get; set; } = default!;
    /// <summary>INN — 9 raqam.</summary>
    public string Tin { get; set; } = default!;

    public string? BranchCode { get; set; }
    public string? BranchName { get; set; }

    /// <summary>Hisob raqami — 20 raqam.</summary>
    public string? Account { get; set; }
    /// <summary>Bank MFO — 5 raqam.</summary>
    public string? BankId { get; set; }
    public string? BankName { get; set; }

    public string? Address { get; set; }
    /// <summary>Hudud kodi (Didox RegionId).</summary>
    public int? RegionId { get; set; }
    public string? RegionName { get; set; }
    /// <summary>Tuman kodi (Didox DistrictCode).</summary>
    public int? DistrictCode { get; set; }
    public string? DistrictName { get; set; }

    /// <summary>OKED — faoliyat turi kodi. Dogovor NK da talab qilinadi.</summary>
    public int? Oked { get; set; }

    // ── Mas'ul shaxslar. Didox ikkalasini ham so'raydi. ──
    public string? DirectorName { get; set; }
    /// <summary>Direktor PINFL — 14 raqam. Dogovor va doverennostda majburiy.</summary>
    public string? DirectorPinfl { get; set; }
    public string? AccountantName { get; set; }
    public string? AccountantPinfl { get; set; }

    /// <summary>QQS to'lovchi ro'yxatga olish kodi (VatRegCode).</summary>
    public string? VatRegCode { get; set; }
    /// <summary>QQS holati (VatRegStatus).</summary>
    public string? VatRegStatus { get; set; }
    /// <summary>Soliq to'lovchi turi (TaxpayerType), masalan 10 — yuridik shaxs.</summary>
    public int TaxpayerType { get; set; } = 10;

    public string? WorkPhone { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }

    /// <summary>Yuk odatda ortiladigan manzil — TTN'da standart qiymat.</summary>
    public string? WarehouseAddress { get; set; }

    /// <summary>
    /// Barcha bildirishnomalar shu Telegram chatiga ketadi.
    /// Botga bir marta /start yozilganda avtomatik yoziladi — kod kerak emas.
    /// Guruh bo'lsa ham bo'ladi: botni guruhga qo'shib /start yozing.
    /// </summary>
    public string? TelegramChatId { get; set; }

    /// <summary>Didox'ga yuborish uchun yetarli ma'lumot bormi.</summary>
    public bool IsReadyForDidox =>
        !string.IsNullOrWhiteSpace(Tin) &&
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(DirectorName) &&
        !string.IsNullOrWhiteSpace(Account) &&
        !string.IsNullOrWhiteSpace(BankId) &&
        !string.IsNullOrWhiteSpace(Address);

    /// <summary>Nima yetishmayotganini ro'yxat qilib qaytaradi (sozlamalar ekrani uchun).</summary>
    public IEnumerable<string> MissingForDidox()
    {
        if (string.IsNullOrWhiteSpace(Tin))           yield return "INN";
        if (string.IsNullOrWhiteSpace(Name))          yield return "Tashkilot nomi";
        if (string.IsNullOrWhiteSpace(DirectorName))  yield return "Direktor F.I.Sh (Didox majburiy talab qiladi)";
        if (string.IsNullOrWhiteSpace(DirectorPinfl)) yield return "Direktor PINFL (dogovor va doverennost uchun)";
        if (string.IsNullOrWhiteSpace(Account))       yield return "Hisob raqami";
        if (string.IsNullOrWhiteSpace(BankId))        yield return "Bank MFO";
        if (string.IsNullOrWhiteSpace(Address))       yield return "Yuridik manzil";
        if (Oked is null)                             yield return "OKED (dogovor NK uchun)";
    }
}

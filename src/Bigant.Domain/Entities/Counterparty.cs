namespace Bigant.Domain.Entities;

/// <summary>
/// Kontragent — postavshik ham, klient ham shu jadvalda.
/// Bitta firma ikkalasi bo'lishi mumkin, shuning uchun ikkita bayroq.
/// </summary>
public class Counterparty
{
    public long Id { get; set; }
    public string Name { get; set; } = default!;

    /// <summary>Yuridik shaxs INN (9 raqam).</summary>
    public string? Inn { get; set; }
    /// <summary>Jismoniy shaxs / YaTT uchun PINFL (14 raqam).</summary>
    public string? Pinfl { get; set; }

    public bool IsSupplier { get; set; }
    public bool IsClient { get; set; }

    public string? BankAccount { get; set; }   // 20 raqamli h/r
    public string? BankMfo { get; set; }       // 5 raqamli MFO
    public string? BankName { get; set; }
    public string? Address { get; set; }
    public string? DirectorName { get; set; }
    public string? Phone { get; set; }

    // ══ Didox majburiy talab qiladigan maydonlar ══
    /// <summary>Direktor PINFL — 14 raqam. Dogovor NK va doverennostda kerak.</summary>
    public string? DirectorPinfl { get; set; }
    /// <summary>Bosh buxgalter — Didox doverennostda so'raydi.</summary>
    public string? AccountantName { get; set; }
    public string? AccountantPinfl { get; set; }

    /// <summary>QQS ro'yxatga olish kodi (Didox: VatRegCode).</summary>
    public string? VatRegCode { get; set; }
    /// <summary>QQS holati (Didox: VatRegStatus).</summary>
    public string? VatRegStatus { get; set; }
    /// <summary>Soliq to'lovchi turi (Didox: TaxpayerType). 10 — yuridik, 30 — boshqa.</summary>
    public int TaxpayerType { get; set; } = 30;
    /// <summary>QQS to'lovchimi. Yo'q bo'lsa fakturada QQS ko'rsatilmaydi.</summary>
    public bool IsVatPayer { get; set; } = true;

    /// <summary>OKED — faoliyat turi kodi. Dogovor NK da talab qilinadi.</summary>
    public int? Oked { get; set; }

    public string? BranchCode { get; set; }
    public string? BranchName { get; set; }

    /// <summary>Didox hudud kodi (RegionId) — TTN yuk manzili uchun.</summary>
    public int? RegionId { get; set; }
    public string? RegionName { get; set; }
    public int? DistrictCode { get; set; }
    public string? DistrictName { get; set; }

    /// <summary>Aloqa uchun mas'ul shaxs (direktor emas).</summary>
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? WorkPhone { get; set; }

    /// <summary>Didox hujjat yuborish uchun yetarlimi.</summary>
    public bool IsReadyForDidox => !MissingForDidox().Any();

    /// <summary>
    /// Nima yetishmayapti. Har biri qaysi hujjatga kerakligi bilan —
    /// bugalter nimadan boshlashni bilsin.
    /// </summary>
    public IEnumerable<string> MissingForDidox()
    {
        if (string.IsNullOrWhiteSpace(Inn) && string.IsNullOrWhiteSpace(Pinfl))
            yield return "INN yoki PINFL";
        if (string.IsNullOrWhiteSpace(Name))          yield return "Nomi";
        if (string.IsNullOrWhiteSpace(DirectorName))  yield return "Direktor F.I.Sh (faktura uchun)";
        // Dogovor NK Owner/Clients blokida FizTin (direktor PINFL) majburiy.
        if (string.IsNullOrWhiteSpace(DirectorPinfl)) yield return "Direktor PINFL (dogovor uchun)";
        if (string.IsNullOrWhiteSpace(BankAccount))   yield return "Hisob raqami";
        if (string.IsNullOrWhiteSpace(BankMfo))       yield return "Bank MFO";
        if (string.IsNullOrWhiteSpace(Address))       yield return "Manzil";
    }

    /// <summary>Otsrochka kunlari. 0 = oldindan to'lov.</summary>
    public int PaymentDays { get; set; }


    /// <summary>Qora ro'yxat — yangi bitim ochilmaydi.</summary>
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

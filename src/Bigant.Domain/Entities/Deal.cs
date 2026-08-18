using Bigant.Domain.Enums;

namespace Bigant.Domain.Entities;

/// <summary>
/// SHARTNOMA KARTOCHKASI — butun tizimning yuragi.
/// "Nechta yopildi / nechta ochiq" savoliga javob shu yerdan chiqadi.
/// </summary>
public class Deal
{
    public long Id { get; set; }
    public DealKind Kind { get; set; }
    public string Number { get; set; } = default!;

    public long CounterpartyId { get; set; }
    public Counterparty Counterparty { get; set; } = default!;

    /// <summary>Bitimni kiritgan xodim. Eslatmadan nusxa unga ham boradi.</summary>
    public long? ManagerId { get; set; }
    public AppUser? Manager { get; set; }

    public DealStatus Status { get; set; } = DealStatus.Draft;

    public decimal AmountTotal { get; set; }
    public decimal AmountPaid { get; set; }

    /// <summary>Sotuv bitimi qaysi xarid bitimidan ta'minlanadi (tranzit savdo).</summary>
    public long? ParentDealId { get; set; }
    public Deal? ParentDeal { get; set; }
    public List<Deal> ChildDeals { get; set; } = [];

    /// <summary>
    /// Dogovorni kim tuzadi.
    ///   true  — biz yuboramiz (chiquvchi)
    ///   false — qarshi tomon yuboradi (kiruvchi)
    ///   null  — standart qoida: sotuvda biz, xaridda ular
    /// Amalda ikkala tomon ham tashabbuskor bo'lishi mumkin, shuning uchun tanlanadi.
    /// </summary>
    public bool? ContractByUs { get; set; }

    /// <summary>
    /// TTN ni kim rasmiylashtiradi — yukni kim tashiyotganiga bog'liq.
    ///   true  — biz tashiymiz, TTN bizdan
    ///   false — qarshi tomon tashiydi, TTN undan keladi
    ///   null  — standart: sotuvda biz, xaridda ular
    /// </summary>
    public bool? TtnByUs { get; set; }

    public DateOnly? ContractDate { get; set; }

    /// <summary>Didox: ContractDoc.ContractName — MAJBURIY.</summary>
    public string? ContractName { get; set; }
    /// <summary>Didox: ContractDoc.ContractPlace — tuzilgan joyi, MAJBURIY.</summary>
    public string? ContractPlace { get; set; }
    /// <summary>Didox: ContractDoc.ContractExpireDate — MAJBURIY.</summary>
    public DateOnly? ContractExpireDate { get; set; }

    /// <summary>Shartnoma amal qilish muddati (ichki hisob uchun).</summary>
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    /// <summary>Didox: Parts[] — shartnoma bo'limlari matni. Bo'sh bo'lsa dogovor yuborilmaydi.</summary>
    public List<ContractPart> Parts { get; set; } = [];

    /// <summary>Yetkazib berish rejalashtirilgan sana — kechikish shundan hisoblanadi.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>
    /// To'lov sharti: otsrochka kunlari. 0 = oldindan to'lov.
    /// Cheklistdagi "To'lov" qadamining muddati shundan hisoblanadi.
    /// </summary>
    public int PaymentDays { get; set; }
    /// <summary>Oldindan to'lov ulushi (%). 100 = to'liq oldindan.</summary>
    public decimal PrepayPercent { get; set; }

    /// <summary>Tovar yetkazib beriladigan manzil — TTN shundan to'ladi.</summary>
    public string? DeliveryAddress { get; set; }
    /// <summary>Yetkazib berish sharti: "o'zi olib ketadi", "biz yetkazamiz", "tashuvchi orqali".</summary>
    public string? DeliveryTerms { get; set; }

    /// <summary>Valyuta. Deyarli doim UZS, lekin import bo'lsa kerak bo'ladi.</summary>
    public string Currency { get; set; } = "UZS";

    public string? Comment { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }

    public List<DealItem> Items { get; set; } = [];
    public List<DealStep> Steps { get; set; } = [];
    public List<Document> Documents { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];

    // ── hisoblanadigan xossalar ─────────────────────────────
    public decimal AmountUnpaid => AmountTotal - AmountPaid;
    public int AgeDays => (int)(DateTimeOffset.UtcNow - CreatedAt).TotalDays;

    /// <summary>Barcha majburiy qadam bajarildimi.</summary>
    public bool AllRequiredStepsDone =>
        Steps.Where(s => s.IsRequired).All(s => s.Status == StepStatus.Done);
}

public class DealItem
{
    public long Id { get; set; }
    public long DealId { get; set; }
    public Deal Deal { get; set; } = default!;

    public long ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public decimal Qty { get; set; }
    public decimal Price { get; set; }
    public decimal VatRate { get; set; } = 12m;


    /// <summary>
    /// Shartnoma paytidagi kurs. Narx у.е. da bo'lsa shu bilan so'mga o'girilgan.
    /// MUZLATILGAN: kurs keyin o'zgarsa ham chiqarilgan faktura o'zgarmaydi.
    /// null = tovar allaqachon so'mda edi.
    /// </summary>
    public decimal? RateSnapshot { get; set; }

    /// <summary>Faktura payloadiga ketadigan qiymatlar — buyurtma paytida muzlatiladi.</summary>
    public string? MxikSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }
    public string? UnitCodeSnapshot { get; set; }
    public string? NameSnapshot { get; set; }

    /// <summary>Qator bo'yicha chegirma summasi (QQS'gacha).</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>Aksiz stavkasi (%) — aksizli tovarlar uchun.</summary>
    public decimal ExciseRate { get; set; }
    public decimal ExciseAmount { get; set; }

    /// <summary>O'rash (upakovka) kodi — soliq klassifikatoridan.</summary>
    public string? PackageCode { get; set; }
    /// <summary>Markirovka belgisi (aksiz markasi, dori, ichimlik). Bo'lmasa bo'sh.</summary>
    public string? MarkingCode { get; set; }

    /// <summary>Chegirmasiz qiymat.</summary>
    public decimal GrossAmount => Math.Round(Qty * Price, 2);
    /// <summary>Chegirma hisobga olingan yakuniy summa (QQS bilan).</summary>
    public decimal Amount => Math.Round(GrossAmount - DiscountAmount, 2);
    public decimal VatAmount => Math.Round(Amount * VatRate / (100m + VatRate), 2);
    /// <summary>QQS'siz yetkazib berish summasi — fakturada alohida ustun.</summary>
    public decimal NetAmount => Math.Round(Amount - VatAmount, 2);
}

/// <summary>
/// Cheklist qatori. Shartnoma yaratilganda shablon bo'yicha avtomatik to'ldiriladi.
/// Hech kim qo'lda "yopildi" deb belgilamaydi — hujjat kelganda o'zi Done bo'ladi.
/// </summary>
public class DealStep
{
    public long Id { get; set; }
    public long DealId { get; set; }
    public Deal Deal { get; set; } = default!;

    public StepType Step { get; set; }
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; } = true;
    public StepStatus Status { get; set; } = StepStatus.Pending;

    public long? DocumentId { get; set; }
    public Document? Document { get; set; }

    /// <summary>Muddat. O'tib ketsa eslatma chiqadi.</summary>
    public DateOnly? DueDate { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Note { get; set; }

    public bool IsOverdue =>
        Status == StepStatus.Pending && DueDate is { } d &&
        d < UzTime.Today;
}

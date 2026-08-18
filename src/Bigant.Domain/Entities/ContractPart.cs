namespace Bigant.Domain.Entities;

/// <summary>
/// Shartnoma bo'limi (Didox: Parts[]).
/// Dogovor NK bo'limlarsiz yuborilmaydi — shuning uchun shablon ishlatiladi:
/// bir marta yoziladi, har shartnomaga nusxalanadi.
/// </summary>
public class ContractPart
{
    public long Id { get; set; }
    public long DealId { get; set; }
    public Deal Deal { get; set; } = default!;

    /// <summary>Didox: ordno.</summary>
    public int OrdNo { get; set; }
    /// <summary>Didox: title — bo'lim nomi.</summary>
    public string Title { get; set; } = default!;
    /// <summary>Didox: body — bo'lim matni.</summary>
    public string Body { get; set; } = default!;
}

/// <summary>
/// Shartnoma bo'limlari shabloni. Bugalter bir marta kiritadi,
/// har yangi shartnomaga avtomatik tushadi — qayta yozilmaydi.
/// </summary>
public class ContractTemplate
{
    public long Id { get; set; }
    public string Name { get; set; } = default!;
    /// <summary>Sotuv uchunmi, xarid uchunmi. null = ikkalasiga.</summary>
    public bool? ForSale { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    public List<ContractTemplatePart> Parts { get; set; } = [];
}

public class ContractTemplatePart
{
    public long Id { get; set; }
    public long TemplateId { get; set; }
    public ContractTemplate Template { get; set; } = default!;

    public int OrdNo { get; set; }
    public string Title { get; set; } = default!;
    /// <summary>
    /// Matn. O'rinbosarlar qo'llab-quvvatlanadi:
    /// {KLIENT}, {BIZ}, {SUMMA}, {SHARTNOMA_RAQAM}, {SANA}, {MUDDAT}
    /// </summary>
    public string Body { get; set; } = default!;
}

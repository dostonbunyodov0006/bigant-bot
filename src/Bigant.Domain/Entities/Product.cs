namespace Bigant.Domain.Entities;

/// <summary>
/// Tovar guruhi. MXIK odatda guruh darajasida bir xil bo'ladi —
/// 5000 tovarga emas, ~200-400 guruhga kod biriktirasiz.
/// </summary>
public class ProductGroup
{
    public long Id { get; set; }
    public string Name { get; set; } = default!;
    public long? ParentId { get; set; }
    public ProductGroup? Parent { get; set; }

    /// <summary>ИКПУ / MXIK — 17 raqam. Tovar o'zida bo'lmasa shundan olinadi.</summary>
    public string? Mxik { get; set; }
    public decimal VatRate { get; set; } = 12m;

    public List<Product> Products { get; set; } = [];
}

public class Product
{
    public long Id { get; set; }
    public long? GroupId { get; set; }
    public ProductGroup? Group { get; set; }

    public string? Article { get; set; }
    public string Name { get; set; } = default!;
    public string? Brand { get; set; }
    public string? Barcode { get; set; }

    /// <summary>Tovarga xos MXIK (Didox: CatalogCode). Bo'sh bo'lsa guruhniki ishlatiladi.</summary>
    public string? Mxik { get; set; }
    /// <summary>MXIK klassifikatoridagi rasmiy nomi (Didox: CatalogName). Kod bilan birga yuboriladi.</summary>
    public string? MxikName { get; set; }

    /// <summary>O'lchov birligi: dona, metr, kg, upak. Santexnikada eng ko'p xato shu yerda.</summary>
    public string Unit { get; set; } = "dona";

    /// <summary>
    /// Didox: MeasureId — o'lchov birligi kodi.
    /// MXIK tanlanmagan bo'lsa MAJBURIY.
    /// </summary>
    public string? MeasureId { get; set; }
    /// <summary>
    /// Didox: PackageCode — o'rash kodi. MXIK tanlangan bo'lsa MAJBURIY.
    /// </summary>
    public string? PackageCode { get; set; }
    /// <summary>Didox: PackageName — "dona", "metr", "quti".</summary>
    public string? PackageName { get; set; }

    /// <summary>1 upak = necha dona/metr.</summary>
    public decimal? PackageQty { get; set; }

    /// <summary>Tovar kelib chiqishi kodi (Didox: Origin). 4 — mahalliy ishlab chiqarish.</summary>
    public int Origin { get; set; } = 4;

    /// <summary>Aksiz stavkasi (%). Aksizsiz tovarda 0.</summary>
    public decimal ExciseRate { get; set; }

    public decimal? VatRate { get; set; }

    /// <summary>
    /// Sotuv narxi — <see cref="Currency"/> da ko'rsatilgan valyutada.
    /// Prays-listlar у.е. da kelgan, shuning uchun konvertatsiya
    /// shartnoma yaratilganda joriy kurs bo'yicha bajariladi.
    /// </summary>
    public decimal BasePrice { get; set; }

    /// <summary>"UZS" yoki "USD" (у.е.). Prays-list у.е. da bo'lsa — USD.</summary>
    public string Currency { get; set; } = "UZS";

    /// <summary>true bo'lsa sotuvchi narxni o'zgartira olmaydi.</summary>
    public bool IsPriceFixed { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;


    /// <summary>Amaldagi MXIK: tovarniki bo'lmasa guruhdan.</summary>
    public string? EffectiveMxik => string.IsNullOrWhiteSpace(Mxik) ? Group?.Mxik : Mxik;
    public decimal EffectiveVatRate => VatRate ?? Group?.VatRate ?? 12m;

    /// <summary>
    /// Didox qoidasi: MXIK berilsa PackageCode majburiy,
    /// MXIK berilmasa MeasureId majburiy. Ikkalasi ham bo'lmasa hujjat rad etiladi.
    /// </summary>
    public bool IsReadyForDidox =>
        (!string.IsNullOrWhiteSpace(EffectiveMxik) && !string.IsNullOrWhiteSpace(PackageCode))
        || (string.IsNullOrWhiteSpace(EffectiveMxik) && !string.IsNullOrWhiteSpace(MeasureId));

    public string? DidoxProblem =>
        !string.IsNullOrWhiteSpace(EffectiveMxik) && string.IsNullOrWhiteSpace(PackageCode)
            ? "MXIK bor, lekin o'rash kodi (PackageCode) yo'q"
        : string.IsNullOrWhiteSpace(EffectiveMxik) && string.IsNullOrWhiteSpace(MeasureId)
            ? "MXIK ham, o'lchov birligi kodi (MeasureId) ham yo'q"
        : null;
}


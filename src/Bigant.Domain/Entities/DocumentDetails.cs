namespace Bigant.Domain.Entities;

/// <summary>
/// DOVERENNOST (M-2 formasi) ma'lumotlari.
/// Bularsiz ishonchnoma haqiqiy emas — shuning uchun alohida jadval,
/// jsonb ichida yashirin qolmasin.
/// </summary>
public class DoverennostInfo
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public Document Document { get; set; } = default!;

    /// <summary>Ro'yxatdan tanlangan xodim. Bir martalik shaxs bo'lsa null.</summary>
    public long? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    // Hujjat chiqarilgan paytdagi holat muzlatiladi — xodim ma'lumoti keyin
    // o'zgarsa ham eski doverennost o'zgarmaydi.
    /// <summary>Didox: Agent.FullName — MAJBURIY.</summary>
    public string EmployeeName { get; set; } = default!;
    /// <summary>Didox: Agent.Pinfl — MAJBURIY, 14 raqam.</summary>
    public string EmployeePinfl { get; set; } = default!;
    public string? Position { get; set; }

    // Passport — Didox'da ixtiyoriy, ichki qog'oz nusxa uchun saqlanadi.
    public string? PassportSeries { get; set; }
    public string? PassportNumber { get; set; }
    public string? PassportIssuedBy { get; set; }
    public DateOnly? PassportIssuedDate { get; set; }

    /// <summary>Didox: EmpowermentDoc.DateOfIssue.</summary>
    public DateOnly IssuedOn { get; set; } = UzTime.Today;

    /// <summary>Kimdan tovar olinadi (kontragent nomi).</summary>
    public string? SupplierName { get; set; }
    /// <summary>Qaysi asosda — shartnoma yoki hisob raqami.</summary>
    public string? BasisDocument { get; set; }

    /// <summary>Amal qilish muddati. O'tib ketsa eslatma chiqadi.</summary>
    public DateOnly ValidUntil { get; set; }

    public bool IsExpired => ValidUntil < UzTime.Today;
}

/// <summary>
/// TTN (tovar-transport nakladnoy) ma'lumotlari.
/// Haydovchi va mashinasiz TTN chiqmaydi.
/// </summary>
public class TtnInfo
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public Document Document { get; set; } = default!;

    public long? DriverId { get; set; }
    public Employee? Driver { get; set; }
    public long? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    // Muzlatilgan nusxa
    public string DriverName { get; set; } = default!;
    /// <summary>Didox: Driver.Pinfl — MAJBURIY.</summary>
    public string? DriverPinfl { get; set; }
    public string? DriverLicense { get; set; }

    /// <summary>Didox: Truck.Model.</summary>
    public string VehicleModel { get; set; } = default!;
    /// <summary>Didox: Truck.RegNo — davlat raqami.</summary>
    public string VehicleNumber { get; set; } = default!;
    /// <summary>Didox: Trailer — bo'lmasa null yuboriladi.</summary>
    public string? TrailerNumber { get; set; }
    public string? TrailerModel { get; set; }

    // ── Yuk ortish nuqtasi (Didox: LoadingPoint) ──
    public string LoadAddress { get; set; } = default!;
    public int? LoadRegionId { get; set; }
    public string? LoadRegionName { get; set; }
    public int? LoadDistrictCode { get; set; }
    public string? LoadDistrictName { get; set; }

    // ── Yuk tushirish nuqtasi (Didox: UnloadingPoint) ──
    public string UnloadAddress { get; set; } = default!;
    public int? UnloadRegionId { get; set; }
    public string? UnloadRegionName { get; set; }
    public int? UnloadDistrictCode { get; set; }
    public string? UnloadDistrictName { get; set; }

    public DateOnly? ShipDate { get; set; }
    /// <summary>Didox: TotalDistance.</summary>
    public decimal? DistanceKm { get; set; }
    /// <summary>Didox: TotalWeightBrutto.</summary>
    public decimal? WeightBrutto { get; set; }
    /// <summary>Didox: TotalWeightNetto.</summary>
    public decimal? WeightNetto { get; set; }

    /// <summary>Didox: TransportType. 1 — avtomobil.</summary>
    public int TransportType { get; set; } = 1;

    /// <summary>Tashuvchi tashkilot (Didox: Carrier). O'zimiz bo'lsak Bigant rekvizitlari.</summary>
    public string? CarrierName { get; set; }
    public string? CarrierInn { get; set; }
    /// <summary>Didox: TotalDeliveryCost.</summary>
    public decimal? FreightCost { get; set; }

    /// <summary>Yuk uchun mas'ul shaxs (Didox: ResponsibleToGoods).</summary>
    public string? ResponsibleName { get; set; }
    public string? ResponsiblePinfl { get; set; }

    /// <summary>Didox'ga yuborish uchun yetarlimi.</summary>
    public bool IsReadyForDidox =>
        !string.IsNullOrWhiteSpace(DriverPinfl) &&
        !string.IsNullOrWhiteSpace(VehicleNumber) &&
        !string.IsNullOrWhiteSpace(VehicleModel) &&
        LoadRegionId is not null && UnloadRegionId is not null;
}

namespace Bigant.Domain.Entities;

/// <summary>
/// Bigant xodimi — ekspeditor, haydovchi, omborchi.
/// Doverennost shu ma'lumotlardan to'ldiriladi, har safar passport qayta yozilmaydi.
/// Tizim foydalanuvchisi bo'lishi shart emas.
/// </summary>
public class Employee
{
    public long Id { get; set; }
    public string FullName { get; set; } = default!;
    public string? Position { get; set; }

    // ── Doverennost uchun ──
    /// <summary>
    /// JShShIR / PINFL — 14 raqam. Didox doverennostda MAJBURIY (Agent.Pinfl).
    /// Passport emas, aynan shu talab qilinadi.
    /// </summary>
    public string? Pinfl { get; set; }

    /// <summary>Passport ma'lumotlari — Didox'da ixtiyoriy, ichki hujjat uchun foydali.</summary>
    public string? PassportSeries { get; set; }
    public string? PassportNumber { get; set; }
    public string? PassportIssuedBy { get; set; }
    public DateOnly? PassportIssuedDate { get; set; }

    public string? Phone { get; set; }

    /// <summary>Haydovchi bo'lsa TTN ro'yxatida chiqadi.</summary>
    public bool IsDriver { get; set; }
    public string? DriverLicense { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Doverennost chiqarish uchun ma'lumot yetarlimi (Didox faqat PINFL so'raydi).</summary>
    public bool CanReceiveGoods =>
        !string.IsNullOrWhiteSpace(Pinfl) && Pinfl.Length == 14;

    /// <summary>TTN'da haydovchi sifatida ko'rsatish mumkinmi.</summary>
    public bool CanDrive => IsDriver && !string.IsNullOrWhiteSpace(Pinfl);
}

/// <summary>
/// Avtomobil — TTN uchun. Bir marta kiritiladi, keyin ro'yxatdan tanlanadi.
/// </summary>
public class Vehicle
{
    public long Id { get; set; }
    /// <summary>Rusumi, masalan "Isuzu NQR 75".</summary>
    public string Model { get; set; } = default!;
    /// <summary>Davlat raqami, masalan "01 A 123 BC".</summary>
    public string PlateNumber { get; set; } = default!;
    public string? TrailerNumber { get; set; }
    /// <summary>Yuk ko'tarish quvvati, tonna.</summary>
    public decimal? CapacityTons { get; set; }

    /// <summary>O'zimizniki emas, tashuvchi tashkilotniki bo'lsa.</summary>
    public string? CarrierName { get; set; }
    public string? CarrierInn { get; set; }

    public bool IsActive { get; set; } = true;
}

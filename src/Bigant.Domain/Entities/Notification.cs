using Bigant.Domain.Enums;

namespace Bigant.Domain.Entities;

/// <summary>
/// Eslatma. "Ko'rmay qolmaslik" uchun — tizimda ham ko'rinadi,
/// Telegram ulansa u yerga ham ketadi.
/// </summary>
public class Notification
{
    public long Id { get; set; }

    public NotificationKind Kind { get; set; }
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;

    /// <summary>Kimga. null = hamma bugalter/rahbarga.</summary>
    public long? UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>
    /// QAYSI ROLGA TEGISHLI.
    ///
    ///   Realizatsiya — pul, qarzdorlik, sotuv hujjatlari
    ///   Perehod      — hujjat zanjiri, kutilayotgan hujjat, ilinmagan hujjat
    ///   null         — ikkalasiga ham tegishli
    ///
    /// Admin har doim HAMMASINI ko'radi.
    /// Maqsad: har bugalterga faqat o'z ishi bo'yicha xabar borsin —
    /// ortiqcha xabar o'qilmay qoladi va muhimini bosib ketadi.
    /// </summary>
    public UserRole? TargetRole { get; set; }

    public string Title { get; set; } = default!;
    public string Body { get; set; } = default!;

    public long? DealId { get; set; }
    public long? DocumentId { get; set; }

    /// <summary>
    /// Takroriy eslatma yuborilmasligi uchun kalit.
    /// Masalan: "PaidNotShipped:deal:1184:2026-08-18"
    /// </summary>
    public string DedupKey { get; set; } = default!;

    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public bool IsPushed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

using Bigant.Domain.Enums;

namespace Bigant.Domain.Entities;

/// <summary>Tizim foydalanuvchisi: bugalter, rahbar, admin.</summary>
public class AppUser
{
    public long Id { get; set; }
    public string Username { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public UserRole Role { get; set; } = UserRole.Realizatsiya;


    /// <summary>Telegram bildirishnomasi uchun. Bo'sh bo'lsa faqat tizim ichida ko'rinadi.</summary>
    public string? TelegramChatId { get; set; }
    /// <summary>Telegram foydalanuvchi nomi — kim ulanganini ko'rish uchun.</summary>
    public string? TelegramUsername { get; set; }
    /// <summary>Foydalanuvchi bildirishnomani o'chirib qo'ygan bo'lsa.</summary>
    public bool TelegramMuted { get; set; }

    /// <summary>
    /// PAROL TANLASHGA QARSHI HIMOYA.
    /// Ketma-ket noto'g'ri urinishlar sanaladi; chegaradan oshsa hisob
    /// vaqtincha bloklanadi. To'g'ri parol kiritilishi bilan nolga tushadi.
    /// IP chegarasi yolg'iz yetarli emas — hujumchi manzilni almashtira oladi.
    /// </summary>
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

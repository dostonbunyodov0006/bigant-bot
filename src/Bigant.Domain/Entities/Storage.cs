namespace Bigant.Domain.Entities;

/// <summary>
/// Hujjat fayli — imzolangan XML, PDF, skanerlangan nusxa.
/// Faylning o'zi diskda/S3 da, bazada faqat ma'lumot.
/// Soliq tekshiruvida imzolangan nusxa talab qilinadi — o'chirilmaydi.
/// </summary>
public class DocumentFile
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public Document Document { get; set; } = default!;

    public string FileName { get; set; } = default!;
    public string MimeType { get; set; } = "application/octet-stream";
    /// <summary>Saqlash kaliti: nisbiy yo'l yoki S3 obyekt nomi.</summary>
    public string StorageKey { get; set; } = default!;
    public long SizeBytes { get; set; }
    /// <summary>SHA-256 — fayl o'zgarmaganini isbotlash uchun.</summary>
    public string? Sha256 { get; set; }

    /// <summary>"signed-xml" | "pdf" | "scan" | "attachment".</summary>
    public string Kind { get; set; } = "attachment";

    public long? UploadedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Didox bilan har bir aloqa yozuvi.
/// Xato chiqqanda "aynan nima yubordik va nima keldi" ni ko'rish uchun.
/// Maxfiy ma'lumot (parol, E-IMZO kaliti) YOZILMAYDI.
/// </summary>
public class IntegrationLog
{
    public long Id { get; set; }

    /// <summary>"didox" | "telegram" | "bank".</summary>
    public string System { get; set; } = "didox";
    public string Operation { get; set; } = default!;   // send | inbox | status | respond

    public long? DocumentId { get; set; }
    /// <summary>Bitta amalning barcha yozuvlarini bog'lash uchun.</summary>
    public Guid CorrelationId { get; set; }

    public string? RequestUrl { get; set; }
    public string? RequestBody { get; set; }
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? Error { get; set; }
    public int DurationMs { get; set; }

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

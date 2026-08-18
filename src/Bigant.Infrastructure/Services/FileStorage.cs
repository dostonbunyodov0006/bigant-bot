using System.Security.Cryptography;
using Bigant.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bigant.Infrastructure.Services;

public sealed class StorageOptions
{
    public const string Section = "Storage";
    /// <summary>Fayllar saqlanadigan papka. Prodda S3/MinIO ga almashtiriladi.</summary>
    public string RootPath { get; set; } = "storage";
    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;
}

/// <summary>
/// Hujjat fayllarini saqlash. Hozir diskda —
/// interfeys orqali S3/MinIO ga o'tish bitta klass almashtirish bilan bo'ladi.
/// </summary>
public interface IFileStorage
{
    Task<DocumentFile> SaveAsync(long documentId, string fileName, string mimeType,
        string kind, byte[] content, long? userId, CancellationToken ct = default);

    Task<(byte[] content, DocumentFile meta)?> ReadAsync(long fileId, CancellationToken ct = default);
}

public sealed class LocalFileStorage : IFileStorage
{
    private readonly AppDbContext _db;
    private readonly StorageOptions _opt;
    private readonly ILogger<LocalFileStorage> _log;

    public LocalFileStorage(AppDbContext db, IOptions<StorageOptions> opt, ILogger<LocalFileStorage> log)
    {
        _db = db; _opt = opt.Value; _log = log;
    }

    public async Task<DocumentFile> SaveAsync(long documentId, string fileName, string mimeType,
        string kind, byte[] content, long? userId, CancellationToken ct = default)
    {
        if (content.Length == 0)
            throw new BusinessException("EMPTY_FILE", "Fayl bo'sh");
        if (content.Length > _opt.MaxFileBytes)
            throw new BusinessException("FILE_TOO_BIG",
                $"Fayl {_opt.MaxFileBytes / 1024 / 1024} MB dan katta");

        // Yil/oy bo'yicha papkalash — bitta papkada million fayl to'planmasin.
        var now = DateTimeOffset.UtcNow;
        var rel = Path.Combine(now.Year.ToString(), now.Month.ToString("00"),
                               $"{documentId}-{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
        var full = Path.Combine(_opt.RootPath, rel);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, content, ct);

        var meta = new DocumentFile
        {
            DocumentId = documentId,
            FileName = Path.GetFileName(fileName),
            MimeType = mimeType,
            StorageKey = rel.Replace('\\', '/'),
            SizeBytes = content.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            Kind = kind,
            UploadedByUserId = userId
        };

        _db.Set<DocumentFile>().Add(meta);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Fayl saqlandi {Key} ({Size} bayt)", meta.StorageKey, meta.SizeBytes);
        return meta;
    }

    public async Task<(byte[] content, DocumentFile meta)?> ReadAsync(long fileId, CancellationToken ct = default)
    {
        var meta = await _db.Set<DocumentFile>().FindAsync([fileId], ct);
        if (meta is null) return null;

        var full = Path.Combine(_opt.RootPath, meta.StorageKey);
        if (!File.Exists(full))
        {
            _log.LogError("Fayl yo'q: {Key}", meta.StorageKey);
            return null;
        }

        return (await File.ReadAllBytesAsync(full, ct), meta);
    }
}

using Microsoft.EntityFrameworkCore;

namespace Bigant.Infrastructure.Services;

/// <summary>Biznes qoidasi buzilganda — API buni 400 ga aylantiradi.</summary>
public sealed class BusinessException : Exception
{
    public string Code { get; }
    public BusinessException(string code, string message) : base(message) => Code = code;
}

/// <summary>
/// Hujjat raqamlarini markazlashgan berish.
/// Bo'shliq va dublikat bo'lmasligi uchun qator qulflanadi.
/// </summary>
public interface INumberGenerator
{
    Task<string> NextAsync(string prefix, CancellationToken ct = default);
}

public sealed class NumberGenerator : INumberGenerator
{
    private readonly AppDbContext _db;
    public NumberGenerator(AppDbContext db) => _db = db;

    public async Task<string> NextAsync(string prefix, CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        var key = $"{prefix}-{year}";

        // UPSERT + RETURNING: bitta atomik so'rov, poyga holati yo'q.
        // ToListAsync ishlatiladi — SingleAsync SQL ustiga LIMIT qo'shmoqchi bo'ladi,
        // INSERT ... RETURNING esa bunga yo'l qo'ymaydi.
        var rows = await _db.Database
            .SqlQueryRaw<long>(
                """
                INSERT INTO number_counters (key, value) VALUES ({0}, 1)
                ON CONFLICT (key) DO UPDATE SET value = number_counters.value + 1
                RETURNING value AS "Value"
                """, key)
            .ToListAsync(ct);

        var value = rows.Count > 0
            ? rows[0]
            : throw new InvalidOperationException($"Raqam hisoblagichi javob bermadi: {key}");

        return $"{prefix}-{year}-{value:D5}";
    }
}

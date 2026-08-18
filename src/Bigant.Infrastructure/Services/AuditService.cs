using System.Text.Json;
using Bigant.Domain.Entities;

namespace Bigant.Infrastructure.Services;

/// <summary>
/// O'zgarmas jurnal. Hech narsa o'chirilmaydi.
/// Soliq tekshiruvida va kontragent bilan nizoda yagona dalilingiz.
/// </summary>
public interface IAuditService
{
    void Write(long? userId, string entity, long? entityId, string action,
               object? oldValue = null, object? newValue = null);
}

public sealed class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    public AuditService(AppDbContext db) => _db = db;

    public void Write(long? userId, string entity, long? entityId, string action,
                      object? oldValue = null, object? newValue = null)
    {
        _db.AuditEntries.Add(new AuditEntry
        {
            UserId = userId,
            Entity = entity,
            EntityId = entityId,
            Action = action,
            OldValue = oldValue is null ? null : JsonSerializer.SerializeToDocument(oldValue),
            NewValue = newValue is null ? null : JsonSerializer.SerializeToDocument(newValue)
        });
    }
}

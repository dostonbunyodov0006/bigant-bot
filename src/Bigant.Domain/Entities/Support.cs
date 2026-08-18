using System.Text.Json;

namespace Bigant.Domain.Entities;

/// <summary>Hujjat raqamlari uchun markazlashgan hisoblagich (dublikat/bo'shliq bo'lmasligi uchun).</summary>
public class NumberCounter
{
    public string Key { get; set; } = default!;   // "SALE-2026", "ESF-2026"
    public long Value { get; set; }
}

/// <summary>O'zgarmas jurnal. Soliq tekshiruvida va nizoda himoyangiz.</summary>
public class AuditEntry
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string Entity { get; set; } = default!;
    public long? EntityId { get; set; }
    public string Action { get; set; } = default!;
    public JsonDocument? OldValue { get; set; }
    public JsonDocument? NewValue { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

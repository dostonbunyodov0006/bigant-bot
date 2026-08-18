using System.Text.Json;
using Bigant.Domain.Enums;

namespace Bigant.Domain.Entities;

/// <summary>
/// Hujjat. Didox yoqilmagan bo'lsa ham to'liq ishlaydi —
/// payload allaqachon Didox formatida saqlanadi, yoqilganda shundoq ketadi.
/// </summary>
public class Document
{
    public long Id { get; set; }

    public long? DealId { get; set; }
    public Deal? Deal { get; set; }

    public DocType DocType { get; set; }
    public DocDirection Direction { get; set; }
    public DocStatus Status { get; set; } = DocStatus.Draft;

    public string? DocNumber { get; set; }
    public DateOnly DocDate { get; set; } = UzTime.Today;

    public long? CounterpartyId { get; set; }
    public Counterparty? Counterparty { get; set; }

    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }

    // ── Didox bilan bog'liq maydonlar (Didox o'chiq bo'lsa bo'sh turadi) ──
    /// <summary>Didox tomonidagi identifikator. Null = hali yuborilmagan / lokal.</summary>
    public string? DidoxId { get; set; }
    /// <summary>PKCS#7 imzo (E-IMZO brauzerdan qaytaradi).</summary>
    public string? SignedData { get; set; }
    public long? SignedByUserId { get; set; }
    public DateTimeOffset? SignedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? RejectReason { get; set; }

    /// <summary>Hujjatning to'liq tanasi — Didox payload formatida (jsonb).</summary>
    public JsonDocument Payload { get; set; } = JsonDocument.Parse("{}");

    public string? FilePath { get; set; }

    /// <summary>Ilingan asos hujjat (masalan ESF qaysi TTN dan chiqqan).</summary>
    public long? ParentDocumentId { get; set; }
    public Document? ParentDocument { get; set; }

    /// <summary>Doverennost bo'lsa — M-2 ma'lumotlari (xodim, passport, muddat).</summary>
    public DoverennostInfo? Doverennost { get; set; }
    /// <summary>TTN bo'lsa — haydovchi, mashina, manzillar.</summary>
    public TtnInfo? Ttn { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsSignable => Status is DocStatus.Draft or DocStatus.ReadyToSign;
    public bool IsSendable => Status == DocStatus.Signed;
}

/// <summary>
/// Chiquvchi navbat. Didox o'chiq bo'lsa ham yozib boriladi —
/// yoqqaningizda navbatdagi hujjatlar o'zi ketadi.
/// IdempotencyKey dublikat schyot-faktura ketishining oldini oladi.
/// </summary>
public class OutboxItem
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public Document Document { get; set; } = default!;

    public Guid IdempotencyKey { get; set; } = Guid.NewGuid();
    public int Attempts { get; set; }
    public DateTimeOffset NextTryAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
}

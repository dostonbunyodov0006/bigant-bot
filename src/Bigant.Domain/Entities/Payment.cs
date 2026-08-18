using Bigant.Domain.Enums;

namespace Bigant.Domain.Entities;

/// <summary>
/// To'lov. Hozircha qo'lda yoki bank vipiskasi importidan.
/// Didox to'lovni bilmaydi — bu butunlay sizning tomoningiz.
/// </summary>
public class Payment
{
    public long Id { get; set; }

    public long CounterpartyId { get; set; }
    public Counterparty Counterparty { get; set; } = default!;

    public long? DealId { get; set; }
    public Deal? Deal { get; set; }

    public PaymentDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PayDate { get; set; }

    public string? BankDocNo { get; set; }
    /// <summary>To'lov maqsadi — avtomatik matching shu matndan shartnoma raqamini qidiradi.</summary>
    public string? Purpose { get; set; }
    /// <summary>"auto" | "manual" — kim bog'lagan.</summary>
    public string MatchedBy { get; set; } = "manual";

    public long? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

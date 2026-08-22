namespace Bigant.Didox;

/// <summary>Didox'ga yuboriladigan hujjat turi (bizning DocType dan mustaqil).</summary>
public enum DidoxDocKind
{
    Contract = 1,
    Doverennost = 2,
    Ttn = 3,
    Esf = 4,
    Act = 5
}

/// <summary>Didox'ga uzatiladigan imzolangan hujjat.</summary>
public sealed record DidoxOutgoingDocument(
    DidoxDocKind Kind,
    string DocNumber,
    DateOnly DocDate,
    string? CounterpartyInn,
    /// <summary>Hujjat tanasi — Didox kutgan JSON sxemasi.</summary>
    string PayloadJson,
    /// <summary>E-IMZO PKCS#7 imzosi. Null bo'lsa Didox rad etadi.</summary>
    string? Pkcs7Signature
);

public sealed record DidoxSendResult(
    bool Success,
    string? DidoxId,
    string? ErrorCode,
    string? ErrorMessage
)
{
    public static DidoxSendResult Ok(string didoxId) => new(true, didoxId, null, null);
    public static DidoxSendResult Fail(string code, string message) => new(false, null, code, message);
}

/// <summary>Didox'dan kelgan hujjat (postavshik dogovori, fakturasi va h.k.).</summary>
/// <summary>
/// Didox'dan kelgan bitta hujjat.
///
/// IKKALA TOMON ham saqlanadi va bu MUHIM: bizga kelgan hujjatda
/// kontragent — YUBORUVCHI, biz yuborgan hujjatda esa — QABUL QILUVCHI.
/// Faqat yuboruvchini olsak, biz yuborgan hujjatlarning «kontragenti»
/// o'zimiz bo'lib chiqardi va butun sotuv tomoni buzilardi.
/// </summary>
public sealed record DidoxInboxItem(
    string DidoxId,
    DidoxDocKind Kind,
    string? DocNumber,
    DateOnly DocDate,
    string? SenderInn,
    string? SenderName,
    decimal? Amount,
    decimal? VatAmount,
    string RawJson,
    DateTimeOffset ReceivedAt,
    string? ReceiverInn = null,
    string? ReceiverName = null
);

public enum DidoxDocState
{
    Unknown = 0,
    Sent = 1,
    Delivered = 2,
    Accepted = 3,
    Rejected = 4,
    Cancelled = 5
}

public sealed record DidoxStatus(
    string DidoxId,
    DidoxDocState State,
    string? RejectReason,
    DateTimeOffset? ChangedAt
);

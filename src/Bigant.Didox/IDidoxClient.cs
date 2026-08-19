namespace Bigant.Didox;

/// <summary>
/// DIDOX ROZETKASI.
///
/// Butun tizim Didox bilan FAQAT shu 4 ta metod orqali gaplashadi.
/// Hozir <see cref="NullDidoxClient"/> ishlaydi (Didox o'chiq).
/// Yoqmoqchi bo'lsangiz: appsettings.json da "Didox:Enabled": true —
/// va <see cref="DidoxHttpClient"/> ishga tushadi. Boshqa hech qanday kod o'zgarmaydi.
/// </summary>
public interface IDidoxClient
{
    /// <summary>Didox yoqilganmi. UI shunga qarab "Didox'ga yuborish" tugmasini ko'rsatadi.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Imzolangan hujjatni yuborish.
    /// <paramref name="idempotencyKey"/> — tarmoq uzilganda dublikat faktura ketmasligi uchun.
    /// </summary>
    Task<DidoxSendResult> SendAsync(
        DidoxOutgoingDocument document,
        Guid idempotencyKey,
        CancellationToken ct = default);

    /// <summary>Bizga kelgan hujjatlarni tortib olish (postavshik dogovori, fakturasi).</summary>
    Task<IReadOnlyList<DidoxInboxItem>> PullInboxAsync(
        DateTimeOffset since,
        CancellationToken ct = default);

    /// <summary>
    /// BIZ YUBORGAN hujjatlarni tortib olish.
    ///
    /// Bugalter hujjatni Didox'ning o'zida yaratadi va yuboradi — bu tizim
    /// uni faqat shu yerdan biladi. Busiz «dogovor tashlandimi?» degan
    /// savolga javob bo'lmaydi va zanjirning yarmi ko'rinmay qoladi.
    /// </summary>
    Task<IReadOnlyList<DidoxInboxItem>> PullOutboxAsync(
        DateTimeOffset since,
        CancellationToken ct = default);

    /// <summary>Yuborilgan hujjat holatini so'rash (qabul qilindimi / rad etildimi).</summary>
    Task<DidoxStatus> GetStatusAsync(string didoxId, CancellationToken ct = default);

    /// <summary>Kelgan hujjatni qabul qilish yoki rad etish.</summary>
    Task<DidoxSendResult> RespondAsync(
        string didoxId,
        bool accept,
        string? reason,
        string? pkcs7Signature,
        CancellationToken ct = default);
}

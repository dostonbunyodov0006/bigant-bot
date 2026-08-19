using Microsoft.Extensions.Logging;

namespace Bigant.Didox;

/// <summary>
/// DIDOX O'CHIQ REJIMI — hozirgi ish rejimi.
///
/// Hujjat tizimda to'liq shakllanadi, imzo talab qilinmaydi,
/// "yuborildi" deb belgilanadi va cheklist qadami yopiladi.
/// Ya'ni butun biznes jarayoni Didox'siz ishlab turadi.
///
/// Didox yoqilganda bu klass DidoxHttpClient ga almashadi —
/// chaqiruvchi kodning bir harfi ham o'zgarmaydi.
/// </summary>
public sealed class NullDidoxClient : IDidoxClient
{
    private readonly ILogger<NullDidoxClient> _log;
    public NullDidoxClient(ILogger<NullDidoxClient> log) => _log = log;

    public bool IsEnabled => false;

    public Task<DidoxSendResult> SendAsync(
        DidoxOutgoingDocument document, Guid idempotencyKey, CancellationToken ct = default)
    {
        // Lokal identifikator — Didox yoqilganda haqiqiy ID bilan almashtiriladi.
        var localId = $"LOCAL-{document.Kind}-{document.DocNumber}-{idempotencyKey:N}";
        if (localId.Length > 60) localId = localId[..60];

        _log.LogInformation(
            "Didox o'chiq. Hujjat lokal yuborilgan deb belgilandi: {Kind} {Number}",
            document.Kind, document.DocNumber);

        return Task.FromResult(DidoxSendResult.Ok(localId));
    }

    public Task<IReadOnlyList<DidoxInboxItem>> PullInboxAsync(
        DateTimeOffset since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DidoxInboxItem>>([]);

    public Task<IReadOnlyList<DidoxInboxItem>> PullOutboxAsync(
        DateTimeOffset since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DidoxInboxItem>>([]);

    public Task<DidoxStatus> GetStatusAsync(string didoxId, CancellationToken ct = default)
        => Task.FromResult(new DidoxStatus(didoxId, DidoxDocState.Delivered, null, DateTimeOffset.UtcNow));

    public Task<DidoxSendResult> RespondAsync(
        string didoxId, bool accept, string? reason, string? pkcs7Signature, CancellationToken ct = default)
        => Task.FromResult(DidoxSendResult.Ok(didoxId));
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bigant.Didox;

/// <summary>
/// HAQIQIY DIDOX KLIENTI — hozircha karkas.
///
/// Yoqish uchun:
///   1) appsettings.json: "Didox": { "Enabled": true, ... }
///   2) Login/Password ni user-secrets ga qo'ying (kodda saqlamang!)
///   3) TODO(didox) belgilangan joylarni rasmiy developer hujjatidagi
///      endpoint nomlari va payload sxemasi bilan to'ldiring.
///
/// Butun qolgan tizim o'zgarmaydi — u faqat IDidoxClient ni ko'radi.
/// </summary>
public sealed class DidoxHttpClient : IDidoxClient
{
    private readonly HttpClient _http;
    private readonly DidoxOptions _opt;
    private readonly ILogger<DidoxHttpClient> _log;

    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public DidoxHttpClient(HttpClient http, IOptions<DidoxOptions> opt, ILogger<DidoxHttpClient> log)
    {
        _opt = opt.Value;
        _log = log;
        _http = http;
        _http.BaseAddress = new Uri(_opt.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_opt.RequestTimeoutSeconds);
    }

    public bool IsEnabled => true;

    // ── Autentifikatsiya ────────────────────────────────────────────
    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-2))
            return _token;

        await _authLock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-2))
                return _token;

            // TODO(didox): rasmiy hujjatdagi auth endpointini qo'ying.
            var resp = await _http.PostAsJsonAsync("v1/auth/login",
                new { login = _opt.Login, password = _opt.Password }, Json, ct);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadFromJsonAsync<AuthResponse>(Json, ct)
                       ?? throw new InvalidOperationException("Didox: auth javobi bosh keldi");

            _token = body.Token;
            _tokenExpiresAt = DateTimeOffset.UtcNow
                .AddMinutes(body.ExpiresInMinutes > 0 ? body.ExpiresInMinutes : 30);
            return _token;
        }
        finally { _authLock.Release(); }
    }

    private async Task<HttpRequestMessage> BuildAsync(HttpMethod m, string url, CancellationToken ct)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));
        return req;
    }

    // ── Yuborish ────────────────────────────────────────────────────
    public async Task<DidoxSendResult> SendAsync(
        DidoxOutgoingDocument document, Guid idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(document.Pkcs7Signature))
            return DidoxSendResult.Fail("NO_SIGNATURE", "Hujjat E-IMZO bilan imzolanmagan");

        try
        {
            // TODO(didox): endpoint va payload sxemasini rasmiy hujjatdan tasdiqlang.
            var req = await BuildAsync(HttpMethod.Post, $"v1/documents/{Endpoint(document.Kind)}", ct);

            // Dublikat faktura ketmasligining kafolati.
            req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString());

            var body = JsonSerializer.Serialize(new
            {
                content = document.PayloadJson,
                sign = document.Pkcs7Signature,
                inn = document.CounterpartyInn
            }, Json);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Didox rad etdi {Status}: {Body}", resp.StatusCode, raw);
                return DidoxSendResult.Fail(((int)resp.StatusCode).ToString(), raw);
            }

            var ok = JsonSerializer.Deserialize<SendResponse>(raw, Json);
            return ok?.DocumentId is { Length: > 0 } id
                ? DidoxSendResult.Ok(id)
                : DidoxSendResult.Fail("NO_ID", raw);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Didox yuborishda xato: {Number}", document.DocNumber);
            return DidoxSendResult.Fail("EXCEPTION", ex.Message);
        }
    }

    // ── Kiruvchi hujjatlar ──────────────────────────────────────────
    public Task<IReadOnlyList<DidoxInboxItem>> PullInboxAsync(
        DateTimeOffset since, CancellationToken ct = default)
        // TODO(didox): inbox endpointi va filtr parametrlarini to'ldiring.
        => PullListAsync($"v1/documents/inbox?from={since:yyyy-MM-dd}", ct);

    public Task<IReadOnlyList<DidoxInboxItem>> PullOutboxAsync(
        DateTimeOffset since, CancellationToken ct = default)
        // TODO(didox): BIZ YUBORGAN hujjatlar ro'yxati endpointi.
        // Bugalter Didox'da yaratgan dogovor/faktura/TTN shu yerdan keladi.
        => PullListAsync($"v1/documents/outbox?from={since:yyyy-MM-dd}", ct);

    /// <summary>Ikkala ro'yxat ham bir xil shaklda qaytadi — o'qish kodi bitta.</summary>
    private async Task<IReadOnlyList<DidoxInboxItem>> PullListAsync(string path, CancellationToken ct)
    {
        var req = await BuildAsync(HttpMethod.Get, path, ct);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var items = await resp.Content.ReadFromJsonAsync<List<InboxDto>>(Json, ct) ?? [];

        return items.Select(i => new DidoxInboxItem(
            i.Id,
            MapKind(i.Type),
            i.Number,
            DateOnly.TryParse(i.Date, out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow),
            i.SenderInn,
            i.SenderName,
            i.Amount,
            i.VatAmount,
            i.Raw ?? "{}",
            DateTimeOffset.UtcNow)).ToList();
    }

    public async Task<DidoxStatus> GetStatusAsync(string didoxId, CancellationToken ct = default)
    {
        var req = await BuildAsync(HttpMethod.Get, $"v1/documents/{didoxId}/status", ct);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return new DidoxStatus(didoxId, DidoxDocState.Unknown, null, null);

        var dto = await resp.Content.ReadFromJsonAsync<StatusDto>(Json, ct);
        return new DidoxStatus(didoxId, MapState(dto?.Status), dto?.RejectReason, DateTimeOffset.UtcNow);
    }

    public async Task<DidoxSendResult> RespondAsync(
        string didoxId, bool accept, string? reason, string? pkcs7Signature, CancellationToken ct = default)
    {
        var req = await BuildAsync(HttpMethod.Post,
            $"v1/documents/{didoxId}/{(accept ? "accept" : "reject")}", ct);
        req.Content = new StringContent(
            JsonSerializer.Serialize(new { sign = pkcs7Signature, reason }, Json),
            Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode
            ? DidoxSendResult.Ok(didoxId)
            : DidoxSendResult.Fail(((int)resp.StatusCode).ToString(),
                                   await resp.Content.ReadAsStringAsync(ct));
    }

    // ── Yordamchilar ────────────────────────────────────────────────
    private static string Endpoint(DidoxDocKind k) => k switch
    {
        DidoxDocKind.Esf         => "invoice",
        DidoxDocKind.Contract    => "contract",
        DidoxDocKind.Doverennost => "empowerment",
        DidoxDocKind.Ttn         => "waybill",
        DidoxDocKind.Act         => "act",
        _ => "document"
    };

    private static DidoxDocKind MapKind(string? t) => t?.ToLowerInvariant() switch
    {
        "invoice" or "esf"              => DidoxDocKind.Esf,
        "contract"                      => DidoxDocKind.Contract,
        "empowerment" or "doverennost"  => DidoxDocKind.Doverennost,
        "waybill" or "ttn"              => DidoxDocKind.Ttn,
        "act"                           => DidoxDocKind.Act,
        _ => DidoxDocKind.Contract
    };

    private static DidoxDocState MapState(string? s) => s?.ToLowerInvariant() switch
    {
        "sent"                 => DidoxDocState.Sent,
        "delivered"            => DidoxDocState.Delivered,
        "accepted" or "signed" => DidoxDocState.Accepted,
        "rejected"             => DidoxDocState.Rejected,
        "cancelled"            => DidoxDocState.Cancelled,
        _ => DidoxDocState.Unknown
    };

    private sealed record AuthResponse(string Token, int ExpiresInMinutes);
    private sealed record SendResponse(string? DocumentId);
    private sealed record StatusDto(string? Status, string? RejectReason);
    private sealed record InboxDto(string Id, string? Type, string? Number, string? Date,
        string? SenderInn, string? SenderName, decimal? Amount, decimal? VatAmount, string? Raw);
}

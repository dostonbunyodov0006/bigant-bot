using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bigant.Infrastructure.Notifications;

public sealed class TelegramOptions
{
    public const string Section = "Telegram";

    public bool Enabled { get; set; }

    /// <summary>
    /// BotFather bergan token. KODDA YOKI appsettings.json DA SAQLAMANG —
    /// `dotnet user-secrets set "Telegram:BotToken" "..."` yoki environment variable.
    /// Token oshkor bo'lsa BotFather'da /revoke qiling.
    /// </summary>
    public string? BotToken { get; set; }

    /// <summary>Bot foydalanuvchi nomi, masalan "bigant_bot" — ulanish havolasi uchun.</summary>
    public string? BotUsername { get; set; }

    /// <summary>Shaxsiy ulanmaganlar uchun umumiy guruh. Bo'sh bo'lsa ishlatilmaydi.</summary>
    public string? DefaultChatId { get; set; }

    /// <summary>Eslatma bosilganda ochiladigan panel manzili.</summary>
    public string? WebAppUrl { get; set; }

    /// <summary>Tinch soatlar — shu oraliqda faqat Critical xabar ketadi (Toshkent vaqti).</summary>
    public int QuietFromHour { get; set; } = 22;
    public int QuietToHour { get; set; } = 8;

    /// <summary>Uzun so'rov (long polling) bilan /start buyrug'ini tinglash.</summary>
    public bool EnablePolling { get; set; } = true;
}

/// <summary>
/// Telegram — FAQAT XABAR KANALI. Ichida buxgalteriya ishi qilinmaydi.
/// Maqsad: ekran yopiq bo'lsa ham "dogovor keldi" / "pul o'tdi" ni ko'rmay qolmaslik.
///
/// Xavfsizlik qoidasi: bot orqali hech qanday tasdiqlash, imzolash yoki
/// to'lov amali bajarilmaydi — faqat panelga havola beriladi.
/// </summary>
public sealed class TelegramChannel : INotificationChannel
{
    private readonly HttpClient _http;
    private readonly TelegramOptions _opt;
    private readonly ILogger<TelegramChannel> _log;

    private static readonly TimeZoneInfo Uz = ResolveTz();

    private readonly IServiceScopeFactory _scopes;
    private string? _companyChat;
    private DateTimeOffset _companyChatAt = DateTimeOffset.MinValue;

    public TelegramChannel(HttpClient http, IOptions<TelegramOptions> opt,
                           ILogger<TelegramChannel> log, IServiceScopeFactory scopes)
    {
        _http = http; _opt = opt.Value; _log = log; _scopes = scopes;
    }

    public string Name => "telegram";

    /// <summary>
    /// Kompaniyaning umumiy chat id si. Botga /start yozilganda yoziladi.
    /// Har xabarda bazaga bormaslik uchun 1 daqiqa keshlanadi.
    /// </summary>
    private async Task<string?> CompanyChatAsync(CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _companyChatAt < TimeSpan.FromMinutes(1))
            return _companyChat;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _companyChat = await db.CompanyProfiles
            .Select(c => c.TelegramChatId)
            .FirstOrDefaultAsync(ct);
        _companyChatAt = DateTimeOffset.UtcNow;
        return _companyChat;
    }

    public async Task<bool> PushAsync(Notification n, AppUser? user, CancellationToken ct = default)
    {
        if (!_opt.Enabled || string.IsNullOrWhiteSpace(_opt.BotToken)) return false;
        if (user is { TelegramMuted: true }) return false;

        // Tunda faqat kritik xabar — odamni behuda uyg'otmaymiz.
        if (IsQuietNow() && n.Severity != NotificationSeverity.Critical) return false;

        // MANZIL. Hujjat aylanishi va to'lov — BUGALTERNING ishi, shuning uchun
        // bunday xabar doim kompaniya chatiga (botga /start yozilgan joyga) boradi.
        // Bitimning mas'ul sotuvchisi bo'lsa — unga ham qo'shimcha nusxa ketadi.
        var company = await CompanyChatAsync(ct) ?? _opt.DefaultChatId;

        var targets = new List<string>();
        if (!string.IsNullOrWhiteSpace(company)) targets.Add(company);
        if (!string.IsNullOrWhiteSpace(user?.TelegramChatId) && user!.TelegramChatId != company)
            targets.Add(user.TelegramChatId!);

        if (targets.Count == 0) return false;

        var text = Render(n);
        var keyboard = BuildKeyboard(n);

        var sent = false;
        foreach (var chatId in targets)
        {
            object payload = keyboard is null
                ? new { chat_id = chatId, text, parse_mode = "HTML", disable_web_page_preview = true }
                : new { chat_id = chatId, text, parse_mode = "HTML", disable_web_page_preview = true,
                        reply_markup = new { inline_keyboard = keyboard } };

            sent |= await SendAsync("sendMessage", payload, ct);
        }
        return sent;
    }

    /// <summary>Ulanish tasdig'i va yordam xabarlari uchun.</summary>
    public Task<bool> SendPlainAsync(string chatId, string html, CancellationToken ct = default)
        => SendAsync("sendMessage", new
        {
            chat_id = chatId, text = html, parse_mode = "HTML", disable_web_page_preview = true
        }, ct);

    /// <summary>
    /// Doimiy tugmalar bilan yuboradi — bugalter buyruq yozib o'tirmaydi,
    /// klaviatura ustidagi tugmani bosadi.
    /// </summary>
    public Task<bool> SendWithMenuAsync(string chatId, string html, CancellationToken ct = default)
        => SendAsync("sendMessage", new
        {
            chat_id = chatId,
            text = html,
            parse_mode = "HTML",
            disable_web_page_preview = true,
            reply_markup = new
            {
                keyboard = Menu,
                resize_keyboard = true,
                is_persistent = true
            }
        }, ct);

    /// <summary>Klaviatura tugmalari. Matni HandleAsync dagi tekshiruv bilan bir xil.</summary>
    public static readonly object[][] Menu =
    [
        [new { text = "\U0001F534 Qarzimiz" }, new { text = "\U0001F7E2 Qarzdorlar" }],
        [new { text = "\U0001F517 Zanjir" }, new { text = "\U0001F4CA Holat" }],
    ];

    private async Task<bool> SendAsync(string method, object payload, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync(
                $"https://api.telegram.org/bot{_opt.BotToken}/{method}", payload, ct);

            if (resp.IsSuccessStatusCode) return true;

            var body = await resp.Content.ReadAsStringAsync(ct);
            // 403 — foydalanuvchi botni bloklagan. Bu xato emas, holat.
            if ((int)resp.StatusCode == 403)
                _log.LogInformation("Telegram: foydalanuvchi botni bloklagan");
            else
                _log.LogWarning("Telegram {Method} javob bermadi: {Status} {Body}",
                    method, resp.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Telegram {Method} xatosi", method);
            return false;
        }
    }

    // ── Xabar ko'rinishi ────────────────────────────────────────────
    private static string Render(Notification n)
    {
        var icon = n.Kind switch
        {
            NotificationKind.PaidNotShipped   => "\U0001F534",   // qizil doira
            NotificationKind.ShippedNotPaid   => "\U0001F4B8",   // uchayotgan pul
            NotificationKind.IncomingDocument => "\U0001F4E5",   // kiruvchi quti
            NotificationKind.DocumentRejected => "❌",       // xoch
            NotificationKind.StepOverdue      => "⏰",       // budilnik
            NotificationKind.DealOverdue      => "⏳",       // qum soati
            NotificationKind.DailyDigest      => "\U0001F4CA",   // diagramma
            NotificationKind.Payment  => "\U0001F4B0",   // pul qopi
            _ => "ℹ"
        };

        var body = Escape(n.Body);
        return $"{icon} <b>{Escape(n.Title)}</b>\n\n{body}";
    }

    /// <summary>
    /// Tugma YO'Q. Veb-panel olib tashlangan — bosiladigan havola qolmadi.
    /// (XATO 2026-08-18: localhost havolasi qo'yilgani uchun Telegram butun
    ///  xabarni rad etardi — «inline keyboard button URL is invalid».)
    /// Kerakli ma'lumot xabar matnida va /zanjir, /qarz buyruqlarida.
    /// </summary>
    private static object[][]? BuildKeyboard(Notification n) => null;

    private bool IsQuietNow()
    {
        var h = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Uz).Hour;
        var (from, to) = (_opt.QuietFromHour, _opt.QuietToHour);
        return from <= to ? h >= from && h < to : h >= from || h < to;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static TimeZoneInfo ResolveTz()
    {
        foreach (var id in new[] { "Asia/Tashkent", "West Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.Utc;
    }
}

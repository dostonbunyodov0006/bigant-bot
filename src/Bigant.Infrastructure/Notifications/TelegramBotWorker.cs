using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;
using Bigant.Domain;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Domain.Workflow;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bigant.Infrastructure.Notifications;

/// <summary>
/// Telegram bot tinglovchisi (long polling).
///
/// Vazifasi:
///   1. /start bilan xabar kanalini belgilash
///   2. Holat va qarzni ko'rsatish
///   3. To'lov kiritish — Didox to'lovni ko'rmaydi, uni faqat odam yozadi
/// </summary>
public sealed partial class TelegramBotWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _httpFactory;
    private readonly TelegramOptions _opt;
    private readonly ILogger<TelegramBotWorker> _log;

    private long _offset;

    /// <summary>
    /// Telegram API snake_case ishlatadi: update_id, chat, from.
    /// XATO (2026-08-18): camelCase bilan o'qilgani uchun UpdateId doim 0 bo'lib,
    /// offset oldinga siljimagan va bot bir xil xabarni cheksiz qayta ishlagan.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public TelegramBotWorker(IServiceScopeFactory scopes, IHttpClientFactory httpFactory,
                             IOptions<TelegramOptions> opt, ILogger<TelegramBotWorker> log)
    {
        _scopes = scopes; _httpFactory = httpFactory; _opt = opt.Value; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_opt.Enabled || !_opt.EnablePolling || string.IsNullOrWhiteSpace(_opt.BotToken))
        {
            _log.LogInformation("Telegram bot tinglovchisi o'chiq");
            return;
        }

        _log.LogInformation("Telegram bot tinglovchisi ishga tushdi");

        await RegisterCommandsAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var before = _offset;
                var updates = await GetUpdatesAsync(ct);

                foreach (var u in updates)
                {
                    _offset = Math.Max(_offset, u.UpdateId + 1);
                    if (u.Message?.Text is { Length: > 0 } text && u.Message.Chat is not null)
                        await HandleAsync(u.Message.Chat.Id.ToString(),
                                          u.Message.From?.Username, text.Trim(), ct);
                }

                // HIMOYA: offset oldinga siljimasa — bir xil xabar cheksiz qayta
                // ishlanadi va foydalanuvchi spam oladi. Bir marta bo'lgan
                // (2026-08-18, snake_case xatosi). Endi darhol to'xtaymiz.
                if (updates.Count > 0 && _offset <= before)
                {
                    _log.LogError(
                        "Telegram: offset oldinga siljimadi ({Offset}) — takroriy ishlov xavfi. " +
                        "Tinglovchi to'xtatildi. getUpdates javob shakli o'zgargan bo'lishi mumkin.",
                        _offset);
                    return;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Telegram polling xatosi — 10 soniyadan keyin qayta urinaman");
                try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { break; }
            }
        }
    }

    /// <summary>
    /// BUYRUQLAR MENYUSI.
    ///
    /// Busiz Telegramda «/» bosilganda hech narsa chiqmasdi — bugalter
    /// bot nima qila olishini bilishi uchun kimdandir so'rashi kerak edi.
    /// Endi ro'yxat izohlari bilan o'zi ko'rinadi.
    ///
    /// Bir marta, bot ishga tushganda yuboriladi. Xato bo'lsa ham bot
    /// ishlashda davom etadi — menyu qulaylik, shart emas.
    /// </summary>
    private async Task RegisterCommandsAsync(CancellationToken ct)
    {
        var commands = new object[]
        {
            new { command = "holat",      description = "Nechta shartnoma ochiq, qancha qarz" },
            new { command = "qarzimiz",   description = "Biz kimga qancha qarzdormiz" },
            new { command = "qarzdorlar", description = "Bizga kim qancha qarzdor" },
            new { command = "qarz",       description = "Ikkala tomonlama qarz ro'yxati" },
            new { command = "zanjir",     description = "Qaysi hujjat qaysi asosda chiqqan" },
            new { command = "tolov",      description = "To'lov kiritish: /tolov 5 mln Uniform" },
            new { command = "yordam",     description = "Bot nima qila oladi" },
            new { command = "start",      description = "Xabarlarni shu chatga ulash" },
            new { command = "ochirish",   description = "Xabarlarni to'xtatish" },
        };

        try
        {
            var http = _httpFactory.CreateClient("telegram-poll");
            using var resp = await http.PostAsJsonAsync(
                $"https://api.telegram.org/bot{_opt.BotToken}/setMyCommands",
                new { commands }, ct);

            if (resp.IsSuccessStatusCode) _log.LogInformation("Telegram buyruqlar menyusi yangilandi");
            else _log.LogWarning("Buyruqlar menyusi o'rnatilmadi: {Status}", resp.StatusCode);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Buyruqlar menyusini o'rnatishda xato");
        }
    }

    private async Task<List<TgUpdate>> GetUpdatesAsync(CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("telegram-poll");
        http.Timeout = TimeSpan.FromSeconds(45);

        var url = $"https://api.telegram.org/bot{_opt.BotToken}/getUpdates" +
                  $"?timeout=30&offset={_offset}&allowed_updates=[\"message\"]";

        var resp = await http.GetFromJsonAsync<TgResponse<List<TgUpdate>>>(url, Json, ct);
        return resp?.Ok == true ? resp.Result ?? [] : [];
    }

    // ── Buyruqlar ───────────────────────────────────────────────────
    private async Task HandleAsync(string chatId, string? username, string text, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tg = scope.ServiceProvider.GetServices<INotificationChannel>()
                      .OfType<TelegramChannel>().FirstOrDefault();
        if (tg is null) return;

        var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var payments = scope.ServiceProvider.GetRequiredService<IPaymentService>();

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant().Split('@')[0];
        var arg = parts.Length > 1 ? parts[1].Trim() : null;

        // Klaviatura tugmalari oddiy matn bo'lib keladi — buyruqqa o'giramiz.
        // Bugalter hech qanday buyruq yozmaydi, faqat tugmani bosadi.
        if (text.Contains("Qarzimiz", StringComparison.OrdinalIgnoreCase)) { cmd = "/qarzimiz"; arg = null; }
        else if (text.Contains("Qarzdorlar", StringComparison.OrdinalIgnoreCase)) { cmd = "/qarzdorlar"; arg = null; }
        else if (text.Contains("Zanjir", StringComparison.OrdinalIgnoreCase) && !text.StartsWith('/')) { cmd = "/zanjir"; arg = null; }
        else if (text.Contains("Holat", StringComparison.OrdinalIgnoreCase) && !text.StartsWith('/')) { cmd = "/holat"; arg = null; }

        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId, ct);

        // ── TELEGRAM AKKAUNTINI O'ZI ULASH ──
        // Chat hali biriktirilmagan bo'lsa, xodimni Telegram USERNAME'i
        // bo'yicha tanib olamiz: admin xodim yaratganda uning @username'ini
        // yozib qo'yadi, xodim esa shunchaki /start bosadi.
        //
        // Ilgari bu yerda kodli ulash bor edi (panelda kod olib, botga yozish).
        // Panel ekrani olib tashlangach kod olish joyi qolmadi va xodim
        // o'z Telegramini umuman ulay olmasdi — rolga ajratish shu sabab
        // amalda ishlamasdi.
        if (user is null && !string.IsNullOrWhiteSpace(username))
        {
            var want = Fold(username);
            var candidates = await db.Users
                .Where(u => u.IsActive && u.TelegramChatId == null && u.TelegramUsername != null)
                .ToListAsync(ct);

            // Solishtirish katta-kichik harf va x/h farqisiz — egasining talabi.
            var match = candidates.FirstOrDefault(u => Fold(u.TelegramUsername!.TrimStart('@')) == want);
            if (match is not null)
            {
                match.TelegramChatId = chatId;
                match.TelegramMuted = false;
                await db.SaveChangesAsync(ct);
                user = match;

                _log.LogInformation("Telegram o'zi ulandi: @{User} → {Login} ({Role})",
                    username, match.Username, match.Role);
            }
        }

        // ══════════════════════════════════════════════════════════
        //  RUXSAT TEKSHIRUVI
        //
        //  Bot username bo'yicha topiladi — begona odam ham yozishi mumkin.
        //  Tekshiruvsiz u soxta to'lov kiritib buxgalteriyani buzardi va
        //  barcha qarzlarni o'qib olardi. (2026-08-19 auditda topilgan.)
        //
        //  Qoida sodda: BIRINCHI /start kanalni egallaydi. Shundan keyin
        //  faqat o'sha chat va ro'yxatdagi xodimlar javob oladi.
        //  Kod kiritish yo'q — egasi shuni so'ragan.
        // ══════════════════════════════════════════════════════════
        var companyChat = await db.CompanyProfiles.Select(p => p.TelegramChatId).FirstOrDefaultAsync(ct);
        var claimed = !string.IsNullOrWhiteSpace(companyChat);
        var allowed = !claimed                       // hali hech kim egallamagan
                   || companyChat == chatId          // kompaniya chati
                   || user is not null;              // ro'yxatdagi xodim

        if (!allowed)
        {
            _log.LogWarning("Telegram: ruxsatsiz chat {ChatId} (@{User}) — «{Text}»",
                chatId, username ?? "-", text.Length > 40 ? text[..40] : text);
            await tg.SendPlainAsync(chatId,
                "Bu bot «BIGANT» MChJ ning ichki xizmat boti.\n" +
                "Sizda kirish huquqi yo'q.", ct);
            return;
        }

        switch (cmd)
        {
            // ── Ulanish: kod yo'q, shunchaki /start ──
            // Bot faqat xabar kanali. Kim yozgani muhim emas — muhimi
            // xabarlar QAYERGA borishi. Birinchi /start shu chatni belgilaydi.
            case "/start":
            {
                var profile = await db.CompanyProfiles.FirstOrDefaultAsync(ct);
                if (profile is null)
                {
                    await tg.SendPlainAsync(chatId,
                        "⚠️ Tizim hali sozlanmagan. Panelga kirib rekvizitlarni to'ldiring.", ct);
                    return;
                }

                // ── UMUMIY KANALNI TORTIB OLISH MUMKIN EMAS ──
                // Ilgari bu yer `profile.TelegramChatId = chatId` deb yozardi.
                // Natijada ro'yxatdagi ikkinchi xodim /start bossa, umumiy
                // kanal jimgina unga ko'chib, EGASI hamma xabarni yo'qotardi.
                // Endi bo'sh kanalgina egallanadi.
                if (string.IsNullOrWhiteSpace(profile.TelegramChatId))
                {
                    profile.TelegramChatId = chatId;
                    await db.SaveChangesAsync(ct);

                    await tg.SendWithMenuAsync(chatId,
                        "✅ <b>Ulandi.</b>\n\n" +
                        "Endi barcha bildirishnomalar shu yerga keladi:\n" +
                        "• Pul keldi, tovar berilmadi\n" +
                        "• To'lov kechikdi\n" +
                        "• Yangi dogovor yoki faktura keldi\n" +
                        "• Hujjat rad etildi\n" +
                        "• Har kuni 09:00 da kunlik xulosa\n\n" +
                        "/holat — hozirgi manzara\n" +
                        "/ochirish — xabarlarni to'xtatish", ct);

                    _log.LogInformation("Telegram umumiy kanali ulandi: chat {ChatId} (@{User})",
                        chatId, username ?? "-");
                    return;
                }

                if (profile.TelegramChatId == chatId)
                {
                    await tg.SendWithMenuAsync(chatId,
                        "✅ Allaqachon ulangan. Xabarlar shu yerga kelmoqda.", ct);
                    return;
                }

                // Umumiy kanal boshqa chatda — bu odam SHAXSIY obunachi.
                // Unga faqat o'z roliga tegishli xabarlar boradi.
                if (user is not null)
                {
                    if (user.TelegramMuted)
                    {
                        user.TelegramMuted = false;
                        await db.SaveChangesAsync(ct);
                    }

                    await tg.SendWithMenuAsync(chatId,
                        $"✅ <b>Ulandi — {RoleTitle(user.Role)}.</b>\n\n" +
                        "Sizga o'z ishingizga tegishli xabarlar keladi.\n\n" +
                        "/holat — hozirgi manzara\n" +
                        "/ochirish — o'zingizga kelishini to'xtatish", ct);

                    _log.LogInformation("Telegram shaxsiy obuna: {User} ({Role}), chat {ChatId}",
                        user.Username, user.Role, chatId);
                }
                return;
            }

            case "/ochirish":
            case "/stop":
            {
                // Har kim FAQAT o'zinikini o'chira oladi. Ilgari bu yer umumiy
                // kanalni kimdan kelganidan qat'i nazar o'chirardi — ya'ni
                // bitta xodim butun kompaniyani xabarsiz qoldira olardi.
                var p = await db.CompanyProfiles.FirstOrDefaultAsync(ct);

                if (p is not null && p.TelegramChatId == chatId)
                {
                    p.TelegramChatId = null;
                    await db.SaveChangesAsync(ct);
                    await tg.SendPlainAsync(chatId,
                        "🔕 Umumiy xabarlar to'xtatildi. Qayta yoqish uchun /start yozing.", ct);
                    return;
                }

                if (user is not null)
                {
                    user.TelegramMuted = true;
                    await db.SaveChangesAsync(ct);
                    await tg.SendPlainAsync(chatId,
                        "🔕 Sizga xabar kelmaydi. Qayta yoqish uchun /start yozing.\n" +
                        "<i>Boshqa xodimlar xabar olishda davom etadi.</i>", ct);
                }
                return;
            }

            // ── Qisqa holat (faqat o'qish) ──
            case "/holat":
                await tg.SendPlainAsync(chatId, await BuildStatusAsync(db, ct), ct);
                return;

            // ── TO'LOV KIRITISH ──
            // Didox to'lovni ko'rmaydi — bugalter shu yerga yozadi.
            case "/tolov":
                await HandlePaymentAsync(db, tg, notify, payments, chatId, arg, ct);
                return;

            // 🔴 QARZIMIZ — biz postavshiklarga qancha qarzdormiz
            case "/qarzimiz":
                await tg.SendWithMenuAsync(chatId,
                    await BuildDebtsAsync(db, DealKind.Purchase, ct), ct);
                return;

            // 🟢 QARZDORLAR — klientlar bizga qancha qarzdor
            case "/qarzdorlar":
                await tg.SendWithMenuAsync(chatId,
                    await BuildDebtsAsync(db, DealKind.Sale, ct), ct);
                return;

            // Ikkalasi birga
            case "/qarz":
                await tg.SendWithMenuAsync(chatId,
                    await BuildDebtsAsync(db, DealKind.Purchase, ct) + "\n" +
                    await BuildDebtsAsync(db, DealKind.Sale, ct), ct);
                return;

            // ── ZANJIR TEKSHIRUVI ──
            // «Aynan o'sha dogovorga doverennost chiqdimi,
            //  aynan o'sha doverennostga faktura chiqdimi.»
            case "/zanjir":
                await tg.SendPlainAsync(chatId, await BuildChainAsync(db, arg, ct), ct);
                return;

            case "/yordam":
            case "/help":
                await tg.SendWithMenuAsync(chatId,
                    "<b>BIGANT — hujjat va qarz nazorati</b>\n\n" +

                    "Bu bot Didox'ni kuzatib turadi. Hujjat kelishi bilan\n" +
                    "sizga xabar beradi va qarzni o'zi hisoblaydi.\n" +
                    "<i>Hujjat yaratish Didox'da qoladi — bot faqat kuzatadi.</i>\n\n" +

                    "━━━━━━━━━━━━━━━━━━━━\n" +
                    "<b>1. O'ZI XABAR BERADI</b>\n" +
                    "Hech narsa so'ramaysiz — o'zi yozadi:\n\n" +
                    "📄 dogovor keldi\n" +
                    "📝 doverennost chiqdi — muddati bilan\n" +
                    "🚚 TTN keldi\n" +
                    "🧾 faktura keldi — QQS bilan\n" +
                    "💰 to'lov o'tdi — qancha qarz qolgani bilan\n" +
                    "⚠️ nomuvofiqlik: summa mos emas, INN boshqa,\n" +
                    "    doverennost muddati o'tgan, tovar bor faktura yo'q\n\n" +

                    "━━━━━━━━━━━━━━━━━━━━\n" +
                    "<b>2. SIZ SO'RAY OLASIZ</b>\n\n" +
                    "/holat — nechta shartnoma ochiq, umumiy qarz\n" +
                    "/qarzimiz — biz kimga qancha qarzdormiz\n" +
                    "/qarzdorlar — bizga kim qancha qarzdor\n" +
                    "/zanjir — qaysi dogovorga qaysi doverennost,\n" +
                    "    qaysi doverennostga qaysi faktura chiqqan\n\n" +

                    "━━━━━━━━━━━━━━━━━━━━\n" +
                    "<b>3. TO'LOV KIRITISH</b>\n" +
                    "Didox to'lovni ko'rmaydi — uni siz yozasiz.\n" +
                    "Buyruq shart emas, shunchaki yozing:\n\n" +
                    "<code>5 mln tolandi Uniformga</code>\n" +
                    "<i>(biz to'ladik)</i>\n\n" +
                    "<code>Uniform bizga 5 mln otkazdi</code>\n" +
                    "<i>(bizga tushdi)</i>\n\n" +
                    "Bot qarzni qayta hisoblab, qancha qolganini aytadi.\n\n" +

                    "━━━━━━━━━━━━━━━━━━━━\n" +
                    "<i>Katta-kichik harf va x/h farqi yo'q —\n" +
                    "«uniform», «UNIFORM», «unihorm» bir xil topiladi.</i>", ct);
                return;

            default:
                if (cmd.StartsWith('/'))
                {
                    await tg.SendPlainAsync(chatId, "Bunday buyruq yo'q. /yordam", ct);
                    return;
                }
                // Buyruqsiz matn — to'lov deb o'qib ko'ramiz.
                // «5 mln tolandi Uzpolimerga» kabi yozuvlar shu yerga tushadi.
                if (LooksLikePayment(text))
                    await HandlePaymentAsync(db, tg, notify, payments, chatId, text, ct);
                return;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  TO'LOV
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Matnda SUMMA bo'lsa — to'lov bo'lishi mumkin.
    /// «to'landi» so'zi SHART EMAS: bugalter «5 mln Uniform» deb ham yozadi.
    /// Kontragent topilsa to'lov deb qabul qilinadi, topilmasa xushmuomala javob.
    /// </summary>
    private static bool LooksLikePayment(string text) => AmountRegex().IsMatch(text);

    private async Task HandlePaymentAsync(AppDbContext db, TelegramChannel tg,
                                          INotificationService notify, IPaymentService payments,
                                          string chatId, string? arg, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            await tg.SendPlainAsync(chatId,
                "Summani va kimga to'langanini yozing:\n" +
                "<code>/tolov 5000000 Uzpolimer</code>\n" +
                "yoki shunchaki <code>5 mln tolandi Uzpolimerga</code>", ct);
            return;
        }

        var amount = ParseAmount(arg);
        if (amount is not { } sum || sum <= 0)
        {
            await tg.SendPlainAsync(chatId, "Summani o'qiy olmadim. Masalan: <code>5 mln</code> yoki <code>5000000</code>", ct);
            return;
        }

        // Summadan keyingi/oldingi so'zlardan kontragent nomini qidiramiz.
        var name = ExtractName(arg);
        if (string.IsNullOrWhiteSpace(name))
        {
            await tg.SendPlainAsync(chatId, "Kimga to'langanini yozing. Masalan: <code>5 mln tolandi Uzpolimerga</code>", ct);
            return;
        }

        var matches = await FindCounterpartiesAsync(db, name, ct);

        if (matches.Count == 0)
        {
            await tg.SendPlainAsync(chatId,
                $"«{Esc(name)}» topilmadi.\n\n" +
                "Kontragent nomini yozing — istalgan tartibda bo'ladi:\n" +
                "<code>5 mln Uniform</code>\n" +
                "<code>Uniform 5 mln</code>\n" +
                "<code>5 mln Uniform MChJ bizga otkazdi</code>", ct);
            return;
        }
        if (matches.Count > 1)
        {
            var list = string.Join("\n", matches.Select(c => "• " + Esc(c.Name)));
            await tg.SendPlainAsync(chatId, $"Bir nechta mos keldi — aniqroq yozing:\n{list}", ct);
            return;
        }

        var cp = matches[0];

        // YO'NALISH. Klient botga hech qachon yozmaydi — ikkala tomonni ham
        // bugalter kiritadi. Shuning uchun matnning o'zidan aniqlaymiz:
        //   «5 mln tolandi Uzpolimerga»        → BIZ to'ladik   (xarid)
        //   «5 mln Uniform MChJ bizga otkazdi» → BIZGA tushdi   (sotuv)
        var weReceived = IncomingWordRegex().IsMatch(arg);
        var wantKind = weReceived ? DealKind.Sale : DealKind.Purchase;

        // Eng eski to'lanmagan ochiq bitim — FIFO, faqat kerakli yo'nalishda.
        var q = db.Deals
            .Include(d => d.Steps)
            .Include(d => d.Payments)
            .Where(d => d.CounterpartyId == cp.Id
                     && d.Status == DealStatus.Active
                     && d.AmountPaid < d.AmountTotal);

        var deal = await q.Where(d => d.Kind == wantKind)
                          .OrderBy(d => d.CreatedAt)
                          .FirstOrDefaultAsync(ct);

        // Kerakli yo'nalishda topilmasa — boshqasini taklif qilamiz, lekin
        // o'zboshimchalik bilan yozmaymiz: noto'g'ri yo'nalish qarzni buzadi.
        if (deal is null)
        {
            var other = await q.OrderBy(d => d.CreatedAt).FirstOrDefaultAsync(ct);
            var hint = other is null
                ? $"{Esc(cp.Name)} bo'yicha to'lanmagan ochiq bitim yo'q."
                : $"{Esc(cp.Name)} bo'yicha " +
                  (weReceived ? "SOTUV" : "XARID") + " shartnomasi topilmadi.\n\n" +
                  $"Ochiq shartnoma: <code>{Esc(other.Number)}</code> " +
                  (other.Kind == DealKind.Sale ? "(bizga qarzdor)" : "(biz qarzdormiz)") + "\n" +
                  (other.Kind == DealKind.Sale
                      ? "Shuni nazarda tutgan bo'lsangiz: <code>5 mln " + Esc(cp.Name) + " bizga otkazdi</code>"
                      : "Shuni nazarda tutgan bo'lsangiz: <code>5 mln tolandi " + Esc(cp.Name) + "ga</code>");
            await tg.SendPlainAsync(chatId, hint, ct);
            return;
        }

        // ── TO'LOVNI XIZMAT ORQALI YOZAMIZ ──
        // Ilgari bot bazaga TO'G'RIDAN-TO'G'RI yozardi va PaymentService dagi
        // qoidalarni chetlab o'tardi: takroriy bank hujjati tekshiruvi,
        // summa nazorati, ortiqcha to'lov ogohlantirishi — hech biri ishlamasdi.
        // Bundan tashqari xabar matni ikki joyda alohida yozilgani uchun
        // bot orqali kiritilgan to'lov boshqacha ko'rinishda kelardi.
        // Endi ikkalasi ham bitta yo'ldan o'tadi.
        var actor = await db.Users
            .FirstOrDefaultAsync(u => (u.Role == UserRole.Realizatsiya || u.Role == UserRole.Admin) && u.IsActive, ct)
            ?? await db.Users.FirstAsync(u => u.IsActive, ct);

        var direction = weReceived ? PaymentDirection.In : PaymentDirection.Out;

        try
        {
            await payments.RegisterAsync(new NewPayment(
                CounterpartyId: cp.Id,
                DealId: deal.Id,
                Direction: direction,
                Amount: sum,
                PayDate: UzTime.Today,
                BankDocNo: null,
                Purpose: "Telegram orqali kiritildi"), actor, ct);
        }
        catch (BusinessException ex)
        {
            await tg.SendPlainAsync(chatId, "⚠️ " + Esc(ex.Message), ct);
            return;
        }

        // Xizmat bitimni yangiladi — qarzning yangi holatini o'qiymiz.
        await db.Entry(deal).ReloadAsync(ct);
        var debt = deal.AmountTotal - deal.AmountPaid;
        var isFull = deal.AmountTotal > 0 && debt <= 0.01m;

        await notify.PushPendingAsync(ct);

        // Yo'nalishga qarab so'zlar o'zgaradi — bugalter nimani yozganini
        // ko'rib, xato kiritgan bo'lsa darhol sezsin.
        // Raqamlar <pre> ichida — ustma-ust turadi, o'qish oson.
        var reply =
            (weReceived
                ? $"\U0001F4B0 <b>QABUL QILINDI</b>\n<i>{Esc(cp.Name)} bizga {sum:N0} so'm o'tkazdi</i>\n\n"
                : $"\U0001F4B8 <b>QABUL QILINDI</b>\n<i>{Esc(cp.Name)} ga {sum:N0} so'm o'tkazdik</i>\n\n") +
            $"Shartnoma <code>{Esc(deal.Number)}</code>\n" +
            "<pre>" +
            $"Summa      {deal.AmountTotal,15:N0}\n" +
            (weReceived
                ? $"Tushdi     {deal.AmountPaid,15:N0}\n"
                : $"O'tkazdik  {deal.AmountPaid,15:N0}\n") +
            "</pre>" +
            (isFull
                ? "✅ <b>QARZ YO'Q</b>"
                : weReceived
                    ? $"\U0001F7E2 <b>QOLDI OLISH: {debt:N0} so'm</b>"
                    : $"\U0001F534 <b>QOLDI TO'LASH: {debt:N0} so'm</b>");

        await tg.SendWithMenuAsync(chatId, reply, ct);
        _log.LogInformation("Telegram orqali to'lov: {Cp} {Sum}", cp.Name, sum);
    }

    // ══════════════════════════════════════════════════════════════
    //  ZANJIR: aynan o'sha dogovorga doverennost, o'shanga faktura
    // ══════════════════════════════════════════════════════════════
    private static async Task<string> BuildChainAsync(AppDbContext db, string? arg, CancellationToken ct)
    {
        var q = db.Deals.AsNoTracking().Include(d => d.Counterparty).AsQueryable();

        // Raqam berilsa — faqat o'sha bitim. Berilmasa — barcha ochiqlari.
        if (!string.IsNullOrWhiteSpace(arg))
        {
            var needle = arg.Trim();
            q = q.Where(d => EF.Functions.ILike(d.Number, $"%{needle}%")
                          || EF.Functions.ILike(d.Counterparty.Name, $"%{needle}%"));
        }
        else
        {
            q = q.Where(d => d.Status == DealStatus.Active);
        }

        var deals = await q.OrderByDescending(d => d.CreatedAt).Take(10).ToListAsync(ct);
        if (deals.Count == 0)
            return string.IsNullOrWhiteSpace(arg)
                ? "Ochiq shartnoma yo'q."
                : $"«{Esc(arg!)}» bo'yicha bitim topilmadi.";

        var ids = deals.Select(d => d.Id).ToList();
        var docs = await db.Documents.AsNoTracking()
            .Where(x => ids.Contains(x.DealId!.Value)
                     && x.Status != DocStatus.Cancelled && x.Status != DocStatus.Rejected)
            .Select(x => new { x.DealId, x.Id, x.DocType, x.DocNumber, x.Direction, x.ParentDocumentId })
            .ToListAsync(ct);

        var sb = new System.Text.StringBuilder();
        var totalBreaks = 0;

        foreach (var d in deals)
        {
            var mine = docs.Where(x => x.DealId == d.Id).ToList();
            var byId = mine.ToDictionary(x => x.Id);

            sb.Append($"\n<b>{Esc(d.Number)}</b> · {Esc(d.Counterparty.Name)}\n");
            if (mine.Count == 0) { sb.Append("   <i>hujjat yo'q</i>\n"); continue; }

            foreach (var x in mine)
            {
                var arrow = x.Direction == DocDirection.Out ? "↑" : "↓";
                var basis = x.ParentDocumentId is { } pid && byId.TryGetValue(pid, out var p)
                    ? $"{DealWorkflow.DocLabel(p.DocType)} №{p.DocNumber}"
                    : null;

                sb.Append($"   {arrow} {Esc(DealWorkflow.DocLabel(x.DocType))} " +
                          $"<code>{Esc(x.DocNumber ?? "—")}</code>\n");
                sb.Append(basis is null
                    ? "      <i>birinchi hujjat</i>\n"
                    : $"      ↳ asos: {Esc(basis)}\n");
            }

            var breaks = DealWorkflow.CheckChain(
                mine.Select(x => (x.DocType, x.Id, x.ParentDocumentId)));

            if (breaks.Count > 0)
            {
                totalBreaks += breaks.Count;
                foreach (var b in breaks)
                    sb.Append($"   ⚠️ <b>{Esc(b.Problem)}</b>\n");
            }
            else
            {
                sb.Append("   ✅ hujjatlari to'liq\n");
            }
        }

        var head = totalBreaks == 0
            ? "<b>Hujjatlar tartibi</b> — hammasi joyida\n"
            : $"<b>Hujjat zanjiri</b> — ⚠️ {totalBreaks} ta uzilish\n";

        return head + sb;
    }

    /// <summary>
    /// Qarzdorlik HAR BIR MChJ uchun ALOHIDA — umumiy yig'indi ko'rsatilmaydi.
    /// Bitta MChJ da bir necha shartnoma bo'lsa, har biri o'z qatorida turadi.
    ///
    /// <paramref name="kind"/>:
    ///   Purchase → «QARZIMIZ»: biz qancha o'tkazganmiz, qancha qolgan
    ///   Sale     → «QARZDORLAR»: bizga qancha o'tkazishgan, qancha qolgan
    /// </summary>
    private static async Task<string> BuildDebtsAsync(AppDbContext db, DealKind kind,
        CancellationToken ct)
    {
        var rows = await db.Deals.AsNoTracking()
            .Where(d => d.Status == DealStatus.Active
                     && d.Kind == kind
                     && d.AmountPaid < d.AmountTotal)
            .Select(d => new
            {
                Name = d.Counterparty.Name,
                d.Number,
                d.AmountTotal,
                d.AmountPaid,
                Debt = d.AmountTotal - d.AmountPaid,
                d.DueDate
            })
            .ToListAsync(ct);

        var buy = kind == DealKind.Purchase;
        var head = buy
            ? "\U0001F534 <b>QARZIMIZ</b>\n<i>biz postavshiklarga qancha to'lashimiz kerak</i>"
            : "\U0001F7E2 <b>QARZDORLAR</b>\n<i>klientlar bizga qancha to'lashi kerak</i>";

        if (rows.Count == 0)
            return head + "\n\n✅ Qarz yo'q.";

        var today = UzTime.Today;
        var sb = new System.Text.StringBuilder(head + "\n");

        var groups = rows.GroupBy(r => r.Name)
                         .OrderByDescending(g => g.Sum(x => x.Debt))
                         .ToList();
        const int Show = 12;

        foreach (var g in groups.Take(Show))
        {
            sb.Append($"\n━━━━━━━━━━━━━━━━━━\n<b>{Esc(g.Key)}</b>\n");
            foreach (var r in g.OrderByDescending(x => x.Debt))
            {
                var late = r.DueDate is { } dd && dd < today
                    ? $"  ⏰ {today.DayNumber - dd.DayNumber} kun kechikdi" : "";
                sb.Append($"<code>{Esc(r.Number)}</code>{late}\n");
                // <pre> — raqamlar ustma-ust, bir qarashda solishtiriladi.
                sb.Append("<pre>");
                sb.Append($"Shartnoma  {r.AmountTotal,15:N0}\n");
                sb.Append(buy ? $"O'tkazdik  {r.AmountPaid,15:N0}\n"
                              : $"Tushdi     {r.AmountPaid,15:N0}\n");
                sb.Append(buy ? $"QOLDI      {r.Debt,15:N0}\n"
                              : $"QOLDI      {r.Debt,15:N0}\n");
                sb.Append("</pre>");
            }
        }

        if (groups.Count > Show)
        {
            var rest = groups.Skip(Show).ToList();
            sb.Append("\n━━━━━━━━━━━━━━━━━━\n");
            sb.Append($"<i>va yana {rest.Count} ta kontragent — ");
            sb.Append($"jami {rest.Sum(x => x.Sum(y => y.Debt)):N0} so'm.\n");
            sb.Append("To'liq ro'yxat dashboardda.</i>\n");
        }

        return sb.ToString();
    }

    // ── Matndan summa va nom ajratish ───────────────────────────────

    /// <summary>«5 mln», «5.5 mln», «5000000», «5 000 000» — hammasini o'qiydi.</summary>
    private static decimal? ParseAmount(string text)
    {
        var m = AmountRegex().Match(text);
        if (!m.Success) return null;

        var raw = m.Groups["num"].Value.Replace(" ", "").Replace(",", ".");
        if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n)) return null;

        var unit = m.Groups["unit"].Value.ToLowerInvariant();
        return unit switch
        {
            "mln" or "million" or "mln." => n * 1_000_000m,
            "ming" or "tys" => n * 1_000m,
            "mlrd" or "milliard" => n * 1_000_000_000m,
            _ => n
        };
    }

    /// <summary>Summani va xizmat so'zlarini olib tashlab, qolgan matnni nom deb oladi.</summary>
    private static string ExtractName(string text)
    {
        var s = AmountRegex().Replace(text, " ");
        s = PaidWordRegex().Replace(s, " ");
        s = NoiseRegex().Replace(s, " ");
        s = s.Trim(' ', '"', '«', '»', ',', '.', '-');
        return TrimSuffix(s).Trim();
    }

    /// <summary>«Uzpolimerga» → «Uzpolimer»: o'zbekcha kelishik qo'shimchalari.</summary>
    private static string TrimSuffix(string s)
    {
        foreach (var suf in new[] { "ning", "dan", "ga", "ka", "qa", "da", "ni" })
            if (s.Length > suf.Length + 2 && s.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                return s[..^suf.Length];
        return s;
    }

    /// <summary>
    /// Kontragentni TOPISHGA HARAKAT QILADI — bugalter qanday yozsa ham.
    /// Bugalter «5 mln Uniform», «Uniform MChJ 5 mln», «5mln uniformga»,
    /// «Uniform 5 000 000 tolandi» — hammasini bir xil tushunishi kerak.
    ///
    /// Uch bosqich, birinchi natija bergani g'olib:
    ///   1) butun qoldiq matn bo'yicha
    ///   2) har bir so'z bo'yicha alohida (kelishik qo'shimchasi kesilgan holda)
    ///   3) nom so'zlarining bosh qismi bo'yicha (ILIKE prefiks)
    /// </summary>
    private static async Task<List<Counterparty>> FindCounterpartiesAsync(
        AppDbContext db, string raw, CancellationToken ct)
    {
        // Kontragentlar ro'yxati kichik (o'nlab), shuning uchun hammasini
        // olib, xotirada YUMSHATILGAN ko'rinishda solishtiramiz. Bu bazadagi
        // ILIKE dan ancha kechirimliroq: katta-kichik harf, qattiq/yumshoq
        // «x»–«h», apostrof, chiziqcha — hech biri to'siq bo'lmaydi.
        var all = await db.Counterparties.AsNoTracking().ToListAsync(ct);

        var found = new Dictionary<long, Counterparty>();
        var needle = Fold(raw);

        void Match(string n)
        {
            if (found.Count > 0 || n.Length < 3) return;
            foreach (var c in all)
            {
                var name = Fold(c.Name);
                if (name.Contains(n, StringComparison.Ordinal) ||
                    n.Contains(name, StringComparison.Ordinal))
                    found.TryAdd(c.Id, c);
            }
        }

        Match(needle);

        var words = raw.Split([' ', '\t', ',', '.', '"', '«', '»', '-'],
                              StringSplitOptions.RemoveEmptyEntries)
                       .Select(x => Fold(TrimSuffix(x)))
                       .Where(x => x.Length >= 3)
                       .ToList();

        // Yozilgan HAMMA so'z nomda uchrasa — mos deb hisoblaymiz.
        // «Buhgalteriya MCHJ» → «Buxgalteriya Servis MChJ» ni topadi:
        // o'rtadagi «Servis» tushib qolgani to'siq bo'lmaydi.
        if (found.Count == 0 && words.Count > 0)
        {
            foreach (var c in all)
            {
                var name = Fold(c.Name);
                if (words.All(w => name.Contains(w, StringComparison.Ordinal)))
                    found.TryAdd(c.Id, c);
            }
        }

        // Oxirgi chora — bitta so'z bo'yicha, eng uzunidan boshlab.
        if (found.Count == 0)
        {
            foreach (var w in words.OrderByDescending(x => x.Length))
            {
                Match(w);
                if (found.Count > 0) break;
            }
        }

        return [.. found.Values];
    }

    /// <summary>
    /// NOMNI YUMSHATISH — bugalter qanday yozsa ham topilsin.
    ///
    /// O'zbek tilida bir xil so'z turlicha yoziladi:
    ///   Buxgalter / Buhgalter,  Toshkent / Toshkent,  O'zbek / Ozbek / Uzbek
    /// Bundan tashqari katta-kichik harf, apostrof turlari (' ' ʻ `),
    /// chiziqcha va bo'shliqlar ham farq qilmasligi kerak.
    ///
    /// Qoida: hammasi kichik harfga, «x» → «h», apostrof va tinish
    /// belgilari olib tashlanadi, «ў/қ/ғ/ҳ» kirill harflari lotinga o'giriladi.
    /// </summary>
    /// <summary>Rol nomi — ulanish tasdig'ida odam o'zini tanib olsin.</summary>
    private static string RoleTitle(UserRole role) => role switch
    {
        UserRole.Realizatsiya => "realizatsiya buxgalteri",
        UserRole.Perehod      => "perehod buxgalteri",
        UserRole.Admin        => "administrator",
        _ => "xodim"
    };

    private static string Fold(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);

        foreach (var ch in s.ToLowerInvariant())
        {
            var c = ch switch
            {
                // Qattiq «x» va yumshoq «h» — bir xil hisoblanadi.
                'x' => 'h',
                // Kirill yozuvidan tushib qolgan harflar.
                'ў' => 'o', 'қ' => 'q', 'ғ' => 'g', 'ҳ' => 'h',
                'а' => 'a', 'б' => 'b', 'в' => 'v', 'г' => 'g', 'д' => 'd',
                'е' => 'e', 'ё' => 'e', 'ж' => 'j', 'з' => 'z', 'и' => 'i',
                'й' => 'y', 'к' => 'k', 'л' => 'l', 'м' => 'm', 'н' => 'n',
                'о' => 'o', 'п' => 'p', 'р' => 'r', 'с' => 's', 'т' => 't',
                'у' => 'u', 'ф' => 'f', 'х' => 'h', 'ц' => 's', 'ч' => 'c',
                'ш' => 's', 'э' => 'e', 'ю' => 'u', 'я' => 'a',
                _ => ch
            };

            // Harf va raqamdan boshqasi tashlanadi: apostrof, chiziqcha,
            // qo'shtirnoq, bo'shliq — nomni ajratishga xalaqit bermasin.
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"(?<num>\d[\d\s.,]*)\s*(?<unit>mln\.?|million|mlrd|milliard|ming|tys|so'?m|sum)?",
        RegexOptions.IgnoreCase)]
    private static partial Regex AmountRegex();

    /// <summary>To'lov ekanini bildiruvchi so'zlar — ikkala yo'nalish uchun.</summary>
    [GeneratedRegex(@"\b(to'?land[iy]|tolandi|to'?lad[iy]k|to'?ladi|o'?tkaz[a-z']*|otkaz[a-z]*|tushdi|keldi|oplata|oplachen|berildi|berdik)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex PaidWordRegex();

    /// <summary>
    /// BIZGA tushgan pulni bildiruvchi so'zlar.
    /// «bizga o'tkazdi», «bizga tushdi», «bizga to'ladi» — sotuv bitimi.
    /// Bo'lmasa — biz to'laganmiz, xarid bitimi.
    /// </summary>
    [GeneratedRegex(@"\b(bizga|hisobimizga|schotimizga|kirim|tushdi|keldi)\b", RegexOptions.IgnoreCase)]
    private static partial Regex IncomingWordRegex();

    [GeneratedRegex(@"\b(uchun|ga|dan|mchj|ooo|bizga|hisobimizga|schotimizga|so'?m|sum|/tolov)\b|[«»""]",
        RegexOptions.IgnoreCase)]
    private static partial Regex NoiseRegex();

    /// <summary>Umumiy manzara. Bot bitta chatga xizmat qiladi — shaxsiy kesim yo'q.</summary>
    private static async Task<string> BuildStatusAsync(AppDbContext db, CancellationToken ct)
    {
        var q = db.Deals.AsNoTracking().Where(d => d.Status == DealStatus.Active);

        var open = await q.CountAsync(ct);
        var openSum = await q.SumAsync(d => (decimal?)d.AmountTotal, ct) ?? 0m;

        var paidNotShipped = await q.CountAsync(d => d.Kind == DealKind.Sale
            && d.Steps.Any(s => s.Step == StepType.Payment && s.Status == StepStatus.Done)
            && d.Steps.Any(s => s.Step == StepType.Ttn && s.Status == StepStatus.Pending), ct);

        var withDebt = await q.CountAsync(d => d.AmountPaid < d.AmountTotal, ct);

        // Umumiy qarz summasi ATAYLAB ko'rsatilmaydi — u hech narsani anglatmaydi.
        // Qaysi MChJ qancha qarz ekani /qarz da alohida chiqadi.
        return $"<b>Holat</b>\n\n" +
               $"Ochiq shartnoma: <b>{open}</b> ({openSum:N0} so'm)\n" +
               $"Qarzdor shartnoma: <b>{withDebt}</b> ta — /qarz\n" +
               (paidNotShipped > 0
                   ? $"\n\U0001F534 Pul keldi, tovar berilmadi: <b>{paidNotShipped}</b> ta"
                   : "\n✅ Shoshilinch holat yo'q");
    }

    private static string RoleName(UserRole r) => r switch
    {
        UserRole.Realizatsiya => "realizatsiya buxgalteri",
        UserRole.Perehod => "perehod buxgalteri",
        _ => "admin"
    };

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ── Telegram API javob shakllari ──
    private sealed record TgResponse<T>(bool Ok, T? Result);
    private sealed record TgUpdate(long UpdateId, TgMessage? Message);
    private sealed record TgMessage(TgChat? Chat, TgUser? From, string? Text);
    private sealed record TgChat(long Id);
    private sealed record TgUser(string? Username);
}

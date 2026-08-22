using Bigant.Domain;
using Bigant.Didox;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Domain.Workflow;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bigant.Infrastructure.Jobs;

/// <summary>
/// KIRUVCHI HUJJATLARNI TORTISH. Didox o'chiq bo'lsa hech narsa qilmaydi
/// (kiruvchi hujjat qo'lda kiritiladi, eslatma baribir ishlaydi).
/// </summary>
public sealed class InboxJob
{
    private readonly IDidoxClient _didox;
    private readonly IInboxService _inbox;
    private readonly INotificationService _notify;
    private readonly ILogger<InboxJob> _log;
    private readonly DidoxOptions _opt;

    public InboxJob(IDidoxClient didox, IInboxService inbox, INotificationService notify,
                    IOptions<DidoxOptions> opt, ILogger<InboxJob> log)
    {
        _didox = didox; _inbox = inbox; _notify = notify; _opt = opt.Value; _log = log;
    }

    /// <summary>
    /// Ketma-ket nechta marta xato bo'lganda ogohlantirish yuboriladi.
    /// Bitta uzilish tarmoqning o'tkinchi nosozligi bo'lishi mumkin —
    /// har mayda xatoda bezovta qilmaymiz.
    /// </summary>
    private const int FailuresBeforeAlarm = 3;
    private static int _consecutiveFailures;

    /// <summary>
    /// Nechanchi uzilish ekani. Ilgari ogohlantirishning takrorlanmaslik
    /// kaliti faqat sanadan tuzilardi — natijada bir kunda IKKINCHI uzilish
    /// bo'lsa xabar JIMGINA yutilardi. Endi har uzilish o'z raqamiga ega.
    /// </summary>
    private static int _outageNo;

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (!_didox.IsEnabled) return;

        // 3 kunlik oyna — dublikat DidoxId indeksi bilan to'siladi.
        var since = DateTimeOffset.UtcNow.AddDays(-3);

        IReadOnlyList<DidoxInboxItem> incoming, outgoing;
        try
        {
            // IKKALA YO'NALISH ham tortiladi:
            //   inbox  — postavshik/klient bizga yuborgani
            //   outbox — bugalter Didox'da yaratib YUBORGANI
            // Busiz «dogovor tashlandimi?» degan savolga javob bo'lmaydi.
            incoming = await _didox.PullInboxAsync(since, ct);
            outgoing = await _didox.PullOutboxAsync(since, ct);
        }
        catch (Exception ex)
        {
            // XAVFLI HOLAT: Didox javob bermasa xabarlar JIMGINA to'xtaydi.
            // Bugalter «hujjat yo'q» deb o'ylaydi, aslida aloqa uzilgan.
            // Shuning uchun adminni ogohlantiramiz.
            _consecutiveFailures++;
            _log.LogError(ex, "Didox javob bermadi ({N}-marta ketma-ket)", _consecutiveFailures);

            if (_consecutiveFailures == FailuresBeforeAlarm)
            {
                _outageNo++;
                await _notify.RaiseAsync(new Notification
                {
                    Kind = NotificationKind.IntegrationDown,
                    TargetRole = UserRole.Admin,
                    Severity = NotificationSeverity.Critical,
                    Title = "\U0001F534 DIDOX ALOQASI UZILDI",
                    Body = $"Didox {FailuresBeforeAlarm} marta ketma-ket javob bermadi.\n\n" +
                           $"Xato: {ex.Message}\n\n" +
                           "⚠️ Yangi hujjatlar KELMAYAPTI.\n" +
                           "Didox sozlamalari va internet aloqasini tekshiring.",
                    DedupKey = $"DidoxDown:{UzTime.Today:yyyy-MM-dd}:{_outageNo}"
                }, ct);
                await _notify.PushPendingAsync(ct);
            }
            return;   // xizmat qulamaydi, keyingi urinishda qayta uriniladi
        }

        // Aloqa tiklandi — agar ilgari uzilgan bo'lsa, xabar beramiz.
        if (_consecutiveFailures >= FailuresBeforeAlarm)
        {
            await _notify.RaiseAsync(new Notification
            {
                Kind = NotificationKind.IntegrationDown,
                TargetRole = UserRole.Admin,
                Severity = NotificationSeverity.Info,
                Title = "✅ DIDOX ALOQASI TIKLANDI",
                Body = "Didox yana javob bermoqda. Hujjatlar qayta kela boshladi.",
                DedupKey = $"DidoxUp:{UzTime.Today:yyyy-MM-dd}:{_outageNo}"
            }, ct);
        }
        _consecutiveFailures = 0;

        var added = 0;
        foreach (var (i, dir) in incoming.Select(x => (x, DocDirection.In))
                        .Concat(outgoing.Select(x => (x, DocDirection.Out))))
        {
            try
            {
                // KONTRAGENT KIM — yo'nalish hal qiladi.
                //   bizga KELDI   → kontragent yuboruvchi
                //   biz YUBORDIK  → kontragent qabul qiluvchi
                // Ikkinchi holatda yuboruvchi BIZ bo'lamiz. Agar shuni
                // hisobga olmasak, biz yuborgan har bir hujjat o'z
                // kompaniyamiz nomiga yozilib, sotuv tomoni butunlay
                // buzilardi: kontragent «BIGANT MChJ», bitim o'zimiz bilan.
                var cpInn  = dir == DocDirection.In ? i.SenderInn  : i.ReceiverInn;
                var cpName = dir == DocDirection.In ? i.SenderName : i.ReceiverName;

                // Himoya to'ri: Didox qaysi maydonni bermasin, O'Z INN'imiz
                // hech qachon kontragent bo'lib qolmasin.
                if (!string.IsNullOrWhiteSpace(_opt.CompanyInn) && cpInn == _opt.CompanyInn)
                {
                    _log.LogWarning(
                        "Didox hujjatida kontragent sifatida o'z INN'imiz keldi: {Id} №{No}. " +
                        "Kontragent aniqlanmadi.", i.DidoxId, i.DocNumber);
                    cpInn = null; cpName = null;
                }

                var doc = await _inbox.RegisterAsync(new IncomingDocument(
                    MapType(i.Kind), i.DidoxId, i.DocNumber, i.DocDate,
                    cpInn, cpName, i.Amount ?? 0m, i.VatAmount ?? 0m, i.RawJson,
                    Direction: dir), ct);
                if (doc is not null) added++;
            }
            catch (Exception ex)
            {
                // BITTA hujjat xato bersa qolganlari baribir kirsin.
                // Aks holda bitta buzuq yozuv butun oqimni to'xtatib qo'yardi.
                _log.LogError(ex, "Didox hujjatini ro'yxatga olib bo'lmadi: {Id} №{No}",
                    i.DidoxId, i.DocNumber);
            }
        }

        if (added > 0)
        {
            _log.LogInformation("Didox'dan {Count} ta hujjat olindi ({In} kiruvchi, {Out} chiquvchi)",
                added, incoming.Count, outgoing.Count);
            await _notify.PushPendingAsync(ct);
        }
    }

    /// <summary>
    /// Didox turini bizning turga o'giradi. Tanimagan turi «Akt» bo'lib
    /// qoladi — bu jimgina bo'lmasin, logga yozamiz: yangi hujjat turi
    /// paydo bo'lganini shundan bilamiz.
    /// </summary>
    private DocType MapType(DidoxDocKind k)
    {
        switch (k)
        {
            case DidoxDocKind.Esf:         return DocType.Esf;
            case DidoxDocKind.Ttn:         return DocType.Ttn;
            case DidoxDocKind.Doverennost: return DocType.Doverennost;
            case DidoxDocKind.Contract:    return DocType.Contract;
            default:
                _log.LogWarning("Didox: tanilmagan hujjat turi «{Kind}» — «Akt» deb yozildi", k);
                return DocType.Act;
        }
    }
}

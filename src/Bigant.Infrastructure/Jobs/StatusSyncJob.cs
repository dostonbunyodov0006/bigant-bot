using Bigant.Didox;
using Bigant.Domain;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Domain.Workflow;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bigant.Infrastructure.Jobs;

/// <summary>
/// HUJJAT HOLATINI KUZATISH.
///
/// Hujjat yuborilishi bilan ish tugamaydi — qarshi tomon uni QABUL QILISHI
/// kerak. Aynan shu kutish paytida oqim to'xtab qoladi va buni hech kim
/// sezmaydi: doverennost muddat bilan chiqadi, muddat tugab ketsa tovar
/// olib bo'lmaydi va hammasi qaytadan boshlanadi.
///
/// Shu sabab Didox'dan hujjat holati so'ralib turiladi va holat o'zgarganda
/// darhol xabar chiqadi: qabul qilindi, rad etildi.
///
/// Didox o'chiq bo'lsa bu vazifa hech narsa qilmaydi — NullDidoxClient
/// «Unknown» qaytaradi va biz holatni o'zgartirmaymiz.
/// </summary>
public sealed class StatusSyncJob
{
    private readonly AppDbContext _db;
    private readonly IDidoxClient _didox;
    private readonly INotificationService _notify;
    private readonly ILogger<StatusSyncJob> _log;
    private readonly DidoxOptions _opt;

    public StatusSyncJob(AppDbContext db, IDidoxClient didox, INotificationService notify,
                         IOptions<DidoxOptions> opt, ILogger<StatusSyncJob> log)
    {
        _db = db; _didox = didox; _notify = notify; _opt = opt.Value; _log = log;
    }

    /// <summary>Yakuniy bo'lmagan holatlar — faqat shular so'raladi.</summary>
    private static readonly DocStatus[] Pending =
        [DocStatus.Sent, DocStatus.Delivered, DocStatus.Signed, DocStatus.ReadyToSign];

    public async Task RunAsync(CancellationToken ct = default)
    {
        // NAVBAT: eng uzoq vaqt tekshirilmagani birinchi keladi.
        //
        // Ilgari bu yer «Id bo'yicha birinchi 200 ta» derdi. Navbatda 200 dan
        // ko'p hujjat bo'lsa, har safar O'SHA 200 tasi so'ralar, qolganlari
        // esa HECH QACHON tekshirilmasdi — ularning rad etilgani ham bilinmay
        // qolardi. (Tekshiruv paytida navbatda 270 ta hujjat bor edi.)
        //
        // Endi tekshirilgan hujjat vaqt bilan belgilanadi va navbat oxiriga
        // tushadi — hammasi navbat bilan tekshiriladi.
        var batch = Math.Clamp(_opt.StatusBatchSize, 10, 500);

        var docs = await _db.Documents
            .Include(d => d.Deal).ThenInclude(x => x!.Counterparty)
            .Where(d => d.DidoxId != null
                     && Pending.Contains(d.Status)
                     && d.Deal!.Status == DealStatus.Active)
            .OrderBy(d => d.UpdatedAt)
            .ThenBy(d => d.Id)
            .Take(batch)
            .ToListAsync(ct);

        if (docs.Count == 0) return;

        var changed = 0;
        foreach (var doc in docs)
        {
            DidoxStatus st;
            try
            {
                st = await _didox.GetStatusAsync(doc.DidoxId!, ct);
            }
            catch (Exception ex)
            {
                // Bitta hujjat holati olinmasa qolganlari to'xtamasin.
                _log.LogWarning(ex, "Holat olinmadi: {DidoxId}", doc.DidoxId);
                continue;
            }

            // Tekshirildi — navbat oxiriga. Holati o'zgarmagan bo'lsa ham
            // belgilanadi, aks holda o'sha hujjat navbat boshida qotib qolardi.
            doc.UpdatedAt = DateTimeOffset.UtcNow;

            var next = Map(st.State);
            if (next is null || next == doc.Status) continue;

            doc.Status = next.Value;
            changed++;

            await RaiseAsync(doc, st.RejectReason, ct);
        }

        // Holat o'zgarmagan bo'lsa ham navbat belgisi saqlanishi kerak.
        await _db.SaveChangesAsync(ct);

        if (changed > 0)
        {
            await _db.SaveChangesAsync(ct);
            await _notify.PushPendingAsync(ct);
            _log.LogInformation("Hujjat holati yangilandi: {Count} ta", changed);
        }
    }

    private static DocStatus? Map(DidoxDocState s) => s switch
    {
        DidoxDocState.Accepted  => DocStatus.Accepted,
        DidoxDocState.Rejected  => DocStatus.Rejected,
        DidoxDocState.Cancelled => DocStatus.Cancelled,
        DidoxDocState.Delivered => DocStatus.Delivered,
        DidoxDocState.Sent      => DocStatus.Sent,
        _ => null,   // Unknown — holatni o'zgartirmaymiz
    };

    /// <summary>
    /// Holat o'zgarganda xabar. Har turi ALOHIDA xabar chiqaradi —
    /// «qabul qilindi» va «rad etildi» bir-birini bosib ketmasin.
    /// </summary>
    private async Task RaiseAsync(Document doc, string? reason, CancellationToken ct)
    {
        var deal = doc.Deal!;
        var who = deal.Counterparty?.Name ?? "—";
        var label = DealWorkflow.DocLabel(doc.DocType);
        var no = doc.DocNumber ?? "—";
        var head = $"Kontragent: {who}\nShartnoma:  {deal.Number}\n\n";

        if (doc.Status == DocStatus.Rejected)
        {
            await _notify.RaiseAsync(new Notification
            {
                Kind = NotificationKind.DocumentRejected,
                TargetRole = UserRole.Perehod,
                Severity = NotificationSeverity.Critical,
                UserId = deal.ManagerId,
                DealId = deal.Id,
                DocumentId = doc.Id,
                Title = "❌ HUJJAT RAD ETILDI",
                Body = head +
                       $"{label} №{no} qabul qilinmadi.\n" +
                       (string.IsNullOrWhiteSpace(reason) ? "Sabab ko'rsatilmagan.\n" : $"Sabab: {reason}\n") +
                       "\nOqim to'xtadi — hujjatni qayta chiqarish kerak.",
                DedupKey = $"Rejected:doc:{doc.Id}"
            }, ct);
            return;
        }

        if (doc.Status != DocStatus.Accepted) return;

        // Qabul qilindi — endi zanjirdagi KEYINGI hujjat kutiladi.
        // Bugalter «endi nima bo'ladi» deb o'ylab qolmasin.
        var nextDoc = DealWorkflow.NextInChain(doc.DocType);
        var nextLine = nextDoc is { } n
            ? $"\nEndi {DealWorkflow.DocLabel(n)} kutiladi."
            : "\nBu oxirgi hujjat — shartnomani yopsa bo'ladi.";

        await _notify.RaiseAsync(new Notification
        {
            Kind = NotificationKind.DocumentAccepted,
            TargetRole = UserRole.Perehod,
            Severity = NotificationSeverity.Info,
            UserId = deal.ManagerId,
            DealId = deal.Id,
            DocumentId = doc.Id,
            Title = "✅ QABUL QILINDI",
            Body = head + $"{label} №{no} — qarshi tomon qabul qildi." + nextLine,
            DedupKey = $"Accepted:doc:{doc.Id}"
        }, ct);
    }
}

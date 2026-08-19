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
/// CHIQUVCHI NAVBAT. Didox o'chiq bo'lsa ham ishlaydi —
/// NullDidoxClient hujjatni lokal "yuborilgan" deb belgilaydi va cheklist yopiladi.
/// </summary>
public sealed class OutboxJob
{
    private readonly AppDbContext _db;
    private readonly IDidoxClient _didox;
    private readonly INotificationService _notify;
    private readonly DidoxOptions _opt;
    private readonly ILogger<OutboxJob> _log;

    public OutboxJob(AppDbContext db, IDidoxClient didox, INotificationService notify,
                     IOptions<DidoxOptions> opt, ILogger<OutboxJob> log)
    {
        _db = db; _didox = didox; _notify = notify; _opt = opt.Value; _log = log;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var batch = await _db.Outbox
            .Include(o => o.Document).ThenInclude(d => d!.Counterparty)
            .Where(o => o.SentAt == null && o.NextTryAt <= now && o.Attempts < _opt.MaxSendAttempts)
            .OrderBy(o => o.Id)
            .Take(50)
            .ToListAsync(ct);

        foreach (var item in batch)
        {
            var doc = item.Document;
            item.Attempts++;

            var outgoing = new DidoxOutgoingDocument(
                MapKind(doc.DocType),
                doc.DocNumber ?? doc.Id.ToString(),
                doc.DocDate,
                doc.Counterparty?.Inn,
                doc.Payload.RootElement.GetRawText(),
                doc.SignedData);

            var result = await _didox.SendAsync(outgoing, item.IdempotencyKey, ct);

            if (result.Success)
            {
                doc.DidoxId = result.DidoxId;
                doc.Status = DocStatus.Sent;
                doc.SentAt = DateTimeOffset.UtcNow;
                doc.UpdatedAt = DateTimeOffset.UtcNow;
                item.SentAt = DateTimeOffset.UtcNow;
                item.LastError = null;

                await CloseStepAsync(doc, ct);
            }
            else
            {
                item.LastError = $"{result.ErrorCode}: {result.ErrorMessage}";
                // Eksponensial kechikish: 1, 2, 4, 8... daqiqa (maks 2 soat).
                var delay = TimeSpan.FromMinutes(Math.Min(120, Math.Pow(2, item.Attempts)));
                item.NextTryAt = DateTimeOffset.UtcNow.Add(delay);

                _log.LogWarning("Hujjat {Doc} yuborilmadi ({Attempt}-urinish): {Err}",
                    doc.DocNumber, item.Attempts, item.LastError);

                if (item.Attempts >= _opt.MaxSendAttempts)
                {
                    await _notify.RaiseAsync(new Notification
                    {
                        Kind = NotificationKind.DocumentRejected,
                        Severity = NotificationSeverity.Critical,
                        DealId = doc.DealId,
                        DocumentId = doc.Id,
                        Title = $"Hujjat yuborilmadi: {doc.DocNumber}",
                        Body = $"{_opt.MaxSendAttempts} marta urinildi. Xato: {item.LastError}",
                        DedupKey = $"SendFailed:doc:{doc.Id}"
                    }, ct);
                }
            }
        }

        if (batch.Count > 0) await _db.SaveChangesAsync(ct);
    }

    private async Task CloseStepAsync(Document doc, CancellationToken ct)
    {
        if (doc.DealId is not { } dealId) return;
        if (DealWorkflow.StepForDocument(doc.DocType) is not { } stepType) return;

        var step = await _db.DealSteps.FirstOrDefaultAsync(s => s.DealId == dealId && s.Step == stepType, ct);
        if (step is not { Status: StepStatus.Pending }) return;

        step.Status = StepStatus.Done;
        step.CompletedAt = DateTimeOffset.UtcNow;
        step.DocumentId = doc.Id;
    }

    private static DidoxDocKind MapKind(DocType t) => t switch
    {
        DocType.Esf         => DidoxDocKind.Esf,
        DocType.Ttn         => DidoxDocKind.Ttn,
        DocType.Doverennost => DidoxDocKind.Doverennost,
        DocType.Contract    => DidoxDocKind.Contract,
        _ => DidoxDocKind.Act
    };
}

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

    public InboxJob(IDidoxClient didox, IInboxService inbox, INotificationService notify,
                    ILogger<InboxJob> log)
    {
        _didox = didox; _inbox = inbox; _notify = notify; _log = log;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (!_didox.IsEnabled) return;

        // 3 kunlik oyna — dublikat DidoxId indeksi bilan to'siladi.
        var since = DateTimeOffset.UtcNow.AddDays(-3);

        // IKKALA YO'NALISH ham tortiladi:
        //   inbox  — postavshik/klient bizga yuborgani
        //   outbox — bugalter Didox'da yaratib YUBORGANI
        // Busiz «dogovor tashlandimi?» degan savolga javob bo'lmaydi.
        var incoming = await _didox.PullInboxAsync(since, ct);
        var outgoing = await _didox.PullOutboxAsync(since, ct);
        var added = 0;

        foreach (var (i, dir) in incoming.Select(x => (x, DocDirection.In))
                        .Concat(outgoing.Select(x => (x, DocDirection.Out))))
        {
            var doc = await _inbox.RegisterAsync(new IncomingDocument(
                MapType(i.Kind), i.DidoxId, i.DocNumber, i.DocDate,
                i.SenderInn, i.SenderName, i.Amount ?? 0m, i.VatAmount ?? 0m, i.RawJson,
                Direction: dir), ct);
            if (doc is not null) added++;
        }

        if (added > 0)
        {
            _log.LogInformation("Didox'dan {Count} ta hujjat olindi ({In} kiruvchi, {Out} chiquvchi)",
                added, incoming.Count, outgoing.Count);
            await _notify.PushPendingAsync(ct);
        }
    }

    private static DocType MapType(DidoxDocKind k) => k switch
    {
        DidoxDocKind.Esf         => DocType.Esf,
        DidoxDocKind.Ttn         => DocType.Ttn,
        DidoxDocKind.Doverennost => DocType.Doverennost,
        DidoxDocKind.Contract    => DocType.Contract,
        _ => DocType.Act
    };
}

/// <summary>Yuborilgan hujjatlar holatini yangilaydi (qabul qilindimi / rad etildimi).</summary>
public sealed class StatusSyncJob
{
    private readonly AppDbContext _db;
    private readonly IDidoxClient _didox;
    private readonly INotificationService _notify;

    public StatusSyncJob(AppDbContext db, IDidoxClient didox, INotificationService notify)
    {
        _db = db; _didox = didox; _notify = notify;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (!_didox.IsEnabled) return;

        var pending = await _db.Documents
            .Where(d => d.DidoxId != null &&
                       (d.Status == DocStatus.Sent || d.Status == DocStatus.Delivered))
            .OrderBy(d => d.UpdatedAt)
            .Take(100)
            .ToListAsync(ct);

        foreach (var doc in pending)
        {
            var st = await _didox.GetStatusAsync(doc.DidoxId!, ct);
            var newStatus = st.State switch
            {
                DidoxDocState.Delivered => DocStatus.Delivered,
                DidoxDocState.Accepted  => DocStatus.Accepted,
                DidoxDocState.Rejected  => DocStatus.Rejected,
                DidoxDocState.Cancelled => DocStatus.Cancelled,
                _ => doc.Status
            };

            if (newStatus == doc.Status) continue;

            doc.Status = newStatus;
            doc.RejectReason = st.RejectReason;
            doc.UpdatedAt = DateTimeOffset.UtcNow;

            if (newStatus == DocStatus.Accepted && doc.DocType == DocType.Esf && doc.DealId is { } dealId)
            {
                var step = await _db.DealSteps
                    .FirstOrDefaultAsync(s => s.DealId == dealId && s.Step == StepType.EsfAccepted, ct);
                if (step is { Status: StepStatus.Pending })
                {
                    step.Status = StepStatus.Done;
                    step.CompletedAt = DateTimeOffset.UtcNow;
                    step.DocumentId = doc.Id;
                }
            }

            if (newStatus == DocStatus.Rejected)
            {
                await _notify.RaiseAsync(new Notification
                {
                    Kind = NotificationKind.DocumentRejected,
                    Severity = NotificationSeverity.Critical,
                    DealId = doc.DealId,
                    DocumentId = doc.Id,
                    Title = $"Hujjat rad etildi: {doc.DocNumber}",
                    Body = st.RejectReason ?? "Sabab ko'rsatilmagan",
                    DedupKey = $"Rejected:doc:{doc.Id}"
                }, ct);
            }
        }

        if (pending.Count > 0) await _db.SaveChangesAsync(ct);
        await _notify.PushPendingAsync(ct);
    }
}

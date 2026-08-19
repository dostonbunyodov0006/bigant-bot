using Bigant.Domain;
using Bigant.Api.Auth;
using Bigant.Didox;
using Bigant.Domain.Enums;
using Bigant.Domain.Workflow;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

/// <summary>
/// KIRUVCHI HUJJATLAR — postavshikdan kelgan dogovor, TTN, faktura;
/// sotuvda esa klientning doverennosti.
///
/// Didox YOQILGAN bo'lsa InboxJob buni o'zi tortadi.
/// Didox O'CHIQ bo'lsa bugalter shu endpoint orqali qo'lda kiritadi —
/// eslatma va cheklist baribir ishlaydi.
/// </summary>
public static class InboxEndpoints
{
    public sealed record RegisterIncoming(
        DocType DocType,
        string? DocNumber,
        DateOnly? DocDate,
        string? SenderInn,
        string? SenderName,
        decimal Amount,
        decimal VatAmount,
        long? DealId,
        /// <summary>Didox javobining xom JSON i. Zanjir havolasi shundan o'qiladi.</summary>
        string? RawJson = null,
        /// <summary>In — bizga keldi, Out — biz yubordik (Didox outbox).</summary>
        DocDirection Direction = DocDirection.In);

    public static void MapInboxEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/inbox").WithTags("Kiruvchi hujjatlar").RequireAuthorization().RequireRateLimiting("api");

        g.MapGet("", async (AppDbContext db, IDidoxClient didox, CancellationToken ct) =>
        {
            var rows = await db.Documents.AsNoTracking()
                .Where(d => d.Direction == DocDirection.In)
                .OrderByDescending(d => d.CreatedAt)
                .Take(200)
                .Select(d => new
                {
                    d.Id, type = d.DocType.ToString(), d.DocNumber, d.DocDate,
                    status = d.Status.ToString(),
                    sender = d.Counterparty != null ? d.Counterparty.Name : null,
                    deal = d.Deal != null ? d.Deal.Number : null, d.DealId,
                    d.Amount, d.DidoxId, d.CreatedAt
                }).ToListAsync(ct);

            return Results.Ok(new { didoxEnabled = didox.IsEnabled, count = rows.Count, items = rows });
        });

        // Qo'lda ro'yxatga olish — Didox o'chiq paytdagi asosiy kirish nuqtasi.
        g.MapPost("", async (RegisterIncoming req, IInboxService inbox, CurrentUser cu, CancellationToken ct) =>
        {
            // ── KIRUVCHI MA'LUMOTNI TEKSHIRISH ──
            // Bu yerga Didox ham, qo'l ham yozadi. Noto'g'ri qiymat o'tib
            // ketsa qarz hisobi buziladi va uni keyin topib bo'lmaydi.
            if (!Enum.IsDefined(req.DocType))
                throw new BusinessException("BAD_DOC_TYPE", "Hujjat turi noto'g'ri");
            if (!Enum.IsDefined(req.Direction))
                throw new BusinessException("BAD_DIRECTION", "Hujjat yo'nalishi noto'g'ri");

            // Manfiy summa qarzni teskari tomonga o'zgartiradi.
            if (req.Amount < 0)
                throw new BusinessException("NEGATIVE_AMOUNT", "Hujjat summasi manfiy bo'lishi mumkin emas");
            if (req.VatAmount < 0)
                throw new BusinessException("NEGATIVE_VAT", "QQS summasi manfiy bo'lishi mumkin emas");
            if (req.VatAmount > req.Amount && req.Amount > 0)
                throw new BusinessException("VAT_TOO_BIG", "QQS umumiy summadan katta bo'lishi mumkin emas");

            // Juda uzoq kelajak yoki o'tmish — sana xato kiritilgan.
            var d = req.DocDate ?? UzTime.Today;
            if (d.Year < 2000 || d > UzTime.Today.AddYears(5))
                throw new BusinessException("BAD_DATE", "Hujjat sanasi haqiqiy emas");

            var doc = await inbox.RegisterAsync(new IncomingDocument(
                req.DocType,
                DidoxId: null,
                req.DocNumber,
                req.DocDate ?? UzTime.Today,
                req.SenderInn,
                req.SenderName,
                req.Amount,
                req.VatAmount,
                RawJson: req.RawJson ?? "{}",
                Direction: req.Direction), ct);

            if (doc is null)
                return Results.Ok(new { skipped = true, message = "Bu hujjat allaqachon ro'yxatda" });

            return Results.Ok(new
            {
                doc.Id, doc.DealId, doc.DocNumber,
                label = DealWorkflow.DocLabel(doc.DocType)
            });
        });

        // Kelgan hujjatni qabul qilish / rad etish.
        g.MapPost("/{id:long}/respond", async (long id, bool accept, string? reason,
            AppDbContext db, IDidoxClient didox, IDealService deals, CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct)
                      ?? throw new BusinessException("NOT_FOUND", "Hujjat topilmadi");
            if (doc.Direction != DocDirection.In)
                throw new BusinessException("NOT_INCOMING", "Bu kiruvchi hujjat emas");

            if (didox.IsEnabled && !string.IsNullOrWhiteSpace(doc.DidoxId))
                await didox.RespondAsync(doc.DidoxId!, accept, reason, doc.SignedData, ct);

            doc.Status = accept ? DocStatus.Accepted : DocStatus.Rejected;
            doc.RejectReason = accept ? null : reason;
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            // Faktura qabul qilinsa cheklistdagi "qabul qilindi" qadami yopiladi.
            if (accept && doc.DocType == DocType.Esf && doc.DealId is { } dealId)
                await deals.CompleteStepAsync(dealId, StepType.EsfAccepted, actor, doc.Id, ct: ct);

            return Results.Ok(new { doc.Id, status = doc.Status.ToString() });
        });
    }
}

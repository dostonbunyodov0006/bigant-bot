using Bigant.Api.Auth;
using Bigant.Didox;
using Bigant.Domain.Enums;
using Bigant.Domain.Workflow;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

public static class DocumentEndpoints
{
    public sealed record CreateDocRequest(long DealId, DocType DocType);
    public sealed record SignRequest(string? Pkcs7Signature);
    public sealed record BatchSignRequest(List<long> DocumentIds, string? Pkcs7Signature);

    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/documents").WithTags("Hujjatlar").RequireAuthorization();

        // ── Bugalter paneli: imzoga tayyor hujjatlar ────────────────
        g.MapGet("/pending-sign", async (AppDbContext db, IDidoxClient didox, CancellationToken ct) =>
        {
            var rows = await db.Documents.AsNoTracking()
                .Where(d => d.Status == DocStatus.Draft || d.Status == DocStatus.ReadyToSign)
                .OrderBy(d => d.CreatedAt)
                .Take(200)
                .Select(d => new
                {
                    d.Id, type = d.DocType.ToString(), d.DocNumber, d.DocDate, d.Amount, d.VatAmount,
                    counterparty = d.Counterparty != null ? d.Counterparty.Name : null,
                    deal = d.Deal != null ? d.Deal.Number : null,
                    d.DealId
                }).ToListAsync(ct);

            return Results.Ok(new
            {
                didoxEnabled = didox.IsEnabled,
                // Didox o'chiq bo'lsa E-IMZO talab qilinmaydi.
                signatureRequired = didox.IsEnabled,
                count = rows.Count,
                items = rows
            });
        });

        g.MapGet("/{id:long}", async (long id, AppDbContext db, CancellationToken ct) =>
        {
            var d = await db.Documents.AsNoTracking()
                .Include(x => x.Counterparty)
                .Include(x => x.Deal)
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new BusinessException("NOT_FOUND", "Hujjat topilmadi");

            return Results.Ok(new
            {
                d.Id, type = d.DocType.ToString(), label = DealWorkflow.DocLabel(d.DocType),
                direction = d.Direction.ToString(), status = d.Status.ToString(),
                d.DocNumber, d.DocDate, d.Amount, d.VatAmount,
                counterparty = d.Counterparty?.Name, deal = d.Deal?.Number, d.DealId,
                d.DidoxId, d.SignedAt, d.SentAt, d.RejectReason,
                payload = d.Payload.RootElement
            });
        });

        // ── Shartnomadan hujjat shakllantirish ──────────────────────
        // Ma'lumot qayta kiritilmaydi — hammasi kartochkadan olinadi.
        g.MapPost("", async (NewDocument req, IDocumentService docs, CurrentUser cu, CancellationToken ct) =>
        {
            var doc = await docs.CreateFromDealAsync(req, await cu.GetAsync(ct), ct);
            return Results.Ok(new
            {
                doc.Id, doc.DocNumber, status = doc.Status.ToString(),
                label = DealWorkflow.DocLabel(doc.DocType)
            });
        });

        // ── Imzolash ────────────────────────────────────────────────
        // Didox o'chiq → Pkcs7Signature kerak emas.
        // Didox yoqilgan → brauzerdagi E-IMZO qaytargan PKCS#7 shu yerga keladi.
        g.MapPost("/{id:long}/sign", async (long id, SignRequest body, IDocumentService docs,
                                            CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            var doc = await docs.MarkSignedAsync(id, body.Pkcs7Signature, actor, ct);
            await docs.EnqueueSendAsync(doc.Id, actor, ct);
            return Results.Ok(new { doc.Id, status = doc.Status.ToString() });
        });

        // ── PAKETLI IMZO — bugalterning kunlik 2 daqiqalik ishi ─────
        g.MapPost("/batch-sign", async (BatchSignRequest body, IDocumentService docs,
                                        CurrentUser cu, CancellationToken ct) =>
        {
            var n = await docs.SignAndSendBatchAsync(body.DocumentIds, body.Pkcs7Signature,
                await cu.GetAsync(ct), ct);
            return Results.Ok(new { signed = n, requested = body.DocumentIds.Count });
        });

        // ── Chiquvchi navbat holati (diagnostika) ───────────────────
        g.MapGet("/outbox", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Outbox.AsNoTracking()
                .Where(o => o.SentAt == null)
                .OrderBy(o => o.NextTryAt)
                .Take(100)
                .Select(o => new
                {
                    o.Id, o.DocumentId, document = o.Document.DocNumber,
                    type = o.Document.DocType.ToString(),
                    o.Attempts, o.NextTryAt, o.LastError
                }).ToListAsync(ct)));
    }
}

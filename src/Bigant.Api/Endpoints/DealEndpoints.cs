using Bigant.Api.Auth;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Domain.Workflow;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

public static class DealEndpoints
{
    public sealed record SkipRequest(string Reason);
    public sealed record CancelRequest(string Reason);
    public sealed record CompleteRequest(long? DocumentId, string? Note);

    /// <summary>
    /// Har rol faqat o'z yo'nalishini ko'radi: realizatsiya — sotuv,
    /// perehod — xarid. Rahbar ikkalasini ham.
    ///
    /// Bu tekshiruv menyuda emas, SERVERDA turishi shart: menyudan
    /// bo'lim olib tashlansa ham, manzilni qo'lda yozgan odam ochib
    /// ko'ra olardi.
    /// </summary>
    private static DealKind? OnlyKind(UserRole role) => role switch
    {
        UserRole.Realizatsiya => DealKind.Sale,
        UserRole.Perehod      => DealKind.Purchase,
        _                     => null,
    };

    private static void EnsureVisible(Deal d, AppUser actor)
    {
        if (OnlyKind(actor.Role) is { } only && d.Kind != only)
            throw new BusinessException("FORBIDDEN",
                only == DealKind.Sale
                    ? "Bu xarid shartnomasi — perehod buxgalterining ishi"
                    : "Bu sotuv shartnomasi — realizatsiya buxgalterining ishi");
    }

    /// <summary>Shartnomani o'zgartirish — faqat rahbar. Buxgalterlarning
    /// ekrani faqat ko'rish uchun: hamma harakat Didoxda bo'ladi.</summary>
    private static void EnsureBoss(AppUser actor)
    {
        if (actor.Role != UserRole.Admin)
            throw new BusinessException("FORBIDDEN",
                "Shartnomani o'zgartirish rahbarning huquqi. Hujjat harakati Didoxda bo'ladi.");
    }

    public static void MapDealEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/deals").WithTags("Shartnomalar").RequireAuthorization().RequireRateLimiting("api");

        // ── Ro'yxat ─────────────────────────────────────────────────
        g.MapGet("", async (DealKind? kind, DealStatus? status, long? counterpartyId,
                            bool? mine, int? take, CurrentUser cu, AppDbContext db, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            var q = db.Deals.AsNoTracking();

            // Rol kesimi — so'rov parametridan qat'i nazar.
            if (OnlyKind(actor.Role) is { } only) q = q.Where(d => d.Kind == only);

            if (mine == true) q = q.Where(d => d.ManagerId == actor.Id);

            if (kind is { } k) q = q.Where(d => d.Kind == k);
            if (status is { } s) q = q.Where(d => d.Status == s);
            if (counterpartyId is { } cid) q = q.Where(d => d.CounterpartyId == cid);

            var rows = await q.OrderByDescending(d => d.CreatedAt)
                .Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(d => new
                {
                    d.Id, d.Number, Kind = d.Kind.ToString(), Status = d.Status.ToString(),
                    counterparty = d.Counterparty.Name,
                    manager = d.Manager != null ? d.Manager.FullName : null,
                    d.AmountTotal, d.AmountPaid, d.CreatedAt, d.ClosedAt,
                    stepsDone = d.Steps.Count(x => x.Status == StepStatus.Done),
                    stepsTotal = d.Steps.Count
                }).ToListAsync(ct);

            return Results.Ok(rows);
        });

        // ── Kartochka + cheklist ────────────────────────────────────
        g.MapGet("/{id:long}", async (long id, IDealService deals, CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            var d = await deals.GetAsync(id, ct)
                    ?? throw new BusinessException("NOT_FOUND", "Shartnoma topilmadi");
            EnsureVisible(d, actor);

            return Results.Ok(new
            {
                d.Id, d.Number, Kind = d.Kind.ToString(), Status = d.Status.ToString(),
                counterparty = new { d.Counterparty.Id, d.Counterparty.Name, d.Counterparty.Inn },
                manager = d.Manager?.FullName,
                d.AmountTotal, d.AmountPaid,
                d.AgeDays,
                d.ContractDate, d.DueDate, d.CreatedAt, d.ClosedAt, d.Comment,

                // Cheklist — "yopildimi" savolining ko'rinishi
                steps = d.Steps.OrderBy(s => s.SortOrder).Select(s => new
                {
                    step = s.Step.ToString(),
                    label = DealWorkflow.StepLabel(s.Step),
                    status = s.Status.ToString(),
                    s.IsRequired, s.DueDate, s.CompletedAt, s.DocumentId, s.IsOverdue, s.Note
                }),

                documents = d.Documents.Select(x => new
                {
                    x.Id, label = DealWorkflow.DocLabel(x.DocType),
                    direction = x.Direction.ToString(), status = x.Status.ToString(),
                    x.DocNumber, x.DocDate, x.Amount, x.DidoxId, x.SentAt, x.RejectReason,
                    x.ParentDocumentId,
                    basis = x.ParentDocument == null ? null
                        : DealWorkflow.DocLabel(x.ParentDocument.DocType) + " №" + x.ParentDocument.DocNumber
                }),

                payments = d.Payments.Select(p => new { p.Id, p.Amount, p.PayDate, p.BankDocNo, p.Purpose })
            });
        });

        // ── Yaratish (sotuvchining asosiy amali) ────────────────────
        g.MapPost("/{id:long}/activate", async (long id, IDealService deals, CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            EnsureBoss(actor);
            var d = await deals.ActivateAsync(id, actor, ct);
            return Results.Ok(new { d.Id, Status = d.Status.ToString() });
        });

        // ── Cheklist qadamini yopish ────────────────────────────────
        g.MapPost("/{id:long}/steps/{step}/complete",
            async (long id, StepType step, CompleteRequest? body, IDealService deals,
                   CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            EnsureBoss(actor);
            var d = await deals.CompleteStepAsync(id, step, actor, body?.DocumentId, body?.Note, ct);
            return Results.Ok(new { d.Id, Status = d.Status.ToString(), d.AllRequiredStepsDone });
        });

        g.MapPost("/{id:long}/steps/{step}/skip",
            async (long id, StepType step, SkipRequest body, IDealService deals,
                   CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            EnsureBoss(actor);
            var d = await deals.SkipStepAsync(id, step, actor, body.Reason, ct);
            return Results.Ok(new { d.Id, Status = d.Status.ToString() });
        });

        g.MapPost("/{id:long}/cancel",
            async (long id, CancelRequest body, IDealService deals, CurrentUser cu, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            EnsureBoss(actor);
            var d = await deals.CancelAsync(id, actor, body.Reason, ct);
            return Results.Ok(new { d.Id, Status = d.Status.ToString() });
        });
    }
}

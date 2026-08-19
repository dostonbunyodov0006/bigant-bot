using Bigant.Domain;
using Bigant.Api.Auth;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

public static class CatalogEndpoints
{
    public sealed record UpsertCounterparty(long? Id, string Name, string? Inn, string? Pinfl,
        bool IsSupplier, bool IsClient, string? BankAccount, string? BankMfo, string? BankName,
        string? Address, string? DirectorName, string? Phone,
        // Didox majburiy maydonlari — bularsiz dogovor va faktura rad etiladi.
        string? DirectorPinfl, string? AccountantName, string? AccountantPinfl,
        int? Oked, string? VatRegCode, string? BranchCode, string? BranchName,
        int? RegionId, string? RegionName, int? DistrictCode, string? DistrictName,
        string? ContactPerson, string? ContactPhone, string? WorkPhone,
        int PaymentDays);

    
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        // ── KONTRAGENTLAR ───────────────────────────────────────────
        var cp = app.MapGroup("/api/counterparties").WithTags("Katalog").RequireAuthorization().RequireRateLimiting("api");

        cp.MapGet("", async (string? q, bool? supplier, bool? client, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Counterparties.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(c => EF.Functions.ILike(c.Name, $"%{q}%") || c.Inn == q);
            if (supplier == true) query = query.Where(c => c.IsSupplier);
            if (client == true) query = query.Where(c => c.IsClient);

            return Results.Ok(await query.OrderBy(c => c.Name).Take(100)
                .Select(c => new
                {
                    c.Id, c.Name, c.Inn, c.IsSupplier, c.IsClient,
                    c.PaymentDays, c.IsBlocked, c.DirectorName, c.DirectorPinfl
                }).ToListAsync(ct));
        });

        // Klientning joriy qarzi — sotuvchi buyurtma kiritishdan oldin ko'radi.
        cp.MapGet("/{id:long}/debt", async (long id, AppDbContext db, CancellationToken ct) =>
        {
            var c = await db.Counterparties.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new BusinessException("NOT_FOUND", "Kontragent topilmadi");

            var open = db.Deals.AsNoTracking()
                .Where(d => d.CounterpartyId == id && d.Status == DealStatus.Active);

            var receivable = await open.Where(d => d.Kind == DealKind.Sale)
                .SumAsync(d => (decimal?)(d.AmountTotal - d.AmountPaid), ct) ?? 0m;
            var payable = await open.Where(d => d.Kind == DealKind.Purchase)
                .SumAsync(d => (decimal?)(d.AmountTotal - d.AmountPaid), ct) ?? 0m;

            var today = UzTime.Today;
            var overdue = await open
                .Where(d => d.Kind == DealKind.Sale
                         && d.Steps.Any(s => s.Step == StepType.Ttn && s.Status == StepStatus.Done)
                         && d.Steps.Any(s => s.Step == StepType.Payment
                                          && s.Status == StepStatus.Pending
                                          && s.DueDate != null && s.DueDate < today))
                .SumAsync(d => (decimal?)(d.AmountTotal - d.AmountPaid), ct) ?? 0m;

            return Results.Ok(new
            {
                c.Id, c.Name, c.PaymentDays, c.IsBlocked,
                receivable, payable, overdue
            });
        });

        cp.MapPost("", async (UpsertCounterparty req, CurrentUser cu, AppDbContext db,
                              IAuditService audit, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (!string.IsNullOrWhiteSpace(req.Inn) && req.Inn.Length != 9)
                throw new BusinessException("BAD_INN", "INN 9 ta raqamdan iborat bo'lishi kerak");

            var e = req.Id is { } id
                ? await db.Counterparties.FirstOrDefaultAsync(x => x.Id == id, ct)
                  ?? throw new BusinessException("NOT_FOUND", "Kontragent topilmadi")
                : new Counterparty();

            e.Name = req.Name; e.Inn = req.Inn; e.Pinfl = req.Pinfl;
            e.IsSupplier = req.IsSupplier; e.IsClient = req.IsClient;
            e.BankAccount = req.BankAccount; e.BankMfo = req.BankMfo; e.BankName = req.BankName;
            e.Address = req.Address; e.DirectorName = req.DirectorName; e.Phone = req.Phone;
            e.DirectorPinfl = req.DirectorPinfl;
            e.AccountantName = req.AccountantName; e.AccountantPinfl = req.AccountantPinfl;
            e.Oked = req.Oked; e.VatRegCode = req.VatRegCode;
            e.BranchCode = req.BranchCode; e.BranchName = req.BranchName;
            e.RegionId = req.RegionId; e.RegionName = req.RegionName;
            e.DistrictCode = req.DistrictCode; e.DistrictName = req.DistrictName;
            e.ContactPerson = req.ContactPerson; e.ContactPhone = req.ContactPhone;
            e.WorkPhone = req.WorkPhone;
            e.PaymentDays = req.PaymentDays;


            if (req.Id is null) db.Counterparties.Add(e);
            audit.Write(actor.Id, nameof(Counterparty), req.Id, req.Id is null ? "create" : "update");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { e.Id });
        });

        // ── TOVAR GURUHLARI ─────────────────────────────────────────
    }
}

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

    public sealed record UpsertProduct(long? Id, long? GroupId, string? Article, string Name,
        string? Brand, string? Barcode,
        // Didox: MXIK berilsa PackageCode majburiy, berilmasa MeasureId majburiy
        string? Mxik, string? MxikName, string Unit,
        string? MeasureId, string? PackageCode, string? PackageName,
        decimal? PackageQty, decimal? VatRate, decimal ExciseRate, decimal BasePrice,
        string? Currency,
        bool IsPriceFixed);


    public static void MapCatalogEndpoints(this WebApplication app)
    {
        // ── KONTRAGENTLAR ───────────────────────────────────────────
        var cp = app.MapGroup("/api/counterparties").WithTags("Katalog").RequireAuthorization();

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
        var gr = app.MapGroup("/api/product-groups").WithTags("Katalog").RequireAuthorization();

        gr.MapGet("", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ProductGroups.AsNoTracking()
                .OrderBy(g => g.Name)
                .Select(g => new { g.Id, g.Name, g.ParentId, g.Mxik, g.VatRate,
                                   products = g.Products.Count })
                .ToListAsync(ct)));

        gr.MapPost("", async (ProductGroup req, CurrentUser cu, AppDbContext db, CancellationToken ct) =>
        {
            if (req.Id == 0) db.ProductGroups.Add(req);
            else db.ProductGroups.Update(req);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { req.Id });
        });

        // ── TOVARLAR ────────────────────────────────────────────────
        var pr = app.MapGroup("/api/products").WithTags("Katalog").RequireAuthorization();

        // 5000+ tovarni tez qidirish (pg_trgm indeksi bilan).
        pr.MapGet("", async (string? q, long? groupId, bool? noMxik, int? take,
                             AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Products.AsNoTracking().Include(p => p.Group).Where(p => p.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(p => EF.Functions.ILike(p.Name, $"%{q}%")
                                      || p.Article == q || p.Barcode == q);
            if (groupId is { } gid) query = query.Where(p => p.GroupId == gid);

            // MXIK to'ldirilmagan tovarlarni topish — katalogni tozalash uchun.
            if (noMxik == true)
                query = query.Where(p => (p.Mxik == null || p.Mxik == "")
                                      && (p.Group == null || p.Group.Mxik == null || p.Group.Mxik == ""));

            var rows = await query.OrderBy(p => p.Name).Take(Math.Clamp(take ?? 50, 1, 500)).ToListAsync(ct);

            return Results.Ok(rows.Select(p => new
            {
                p.Id, p.Article, p.Name, p.Brand, p.Unit, p.BasePrice, p.Currency,
                p.IsPriceFixed,
                mxik = p.EffectiveMxik,
                vatRate = p.EffectiveVatRate,
                group = p.Group?.Name
            }));
        });

        pr.MapPost("", async (UpsertProduct req, CurrentUser cu, AppDbContext db,
                              IAuditService audit, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (!string.IsNullOrWhiteSpace(req.Mxik) && req.Mxik.Length != 17)
                throw new BusinessException("BAD_MXIK", "MXIK 17 ta raqamdan iborat bo'ladi");

            var p = req.Id is { } id
                ? await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct)
                  ?? throw new BusinessException("NOT_FOUND", "Tovar topilmadi")
                : new Product();

            p.GroupId = req.GroupId; p.Article = req.Article; p.Name = req.Name;
            p.Brand = req.Brand; p.Barcode = req.Barcode; p.Mxik = req.Mxik;
            p.MxikName = req.MxikName; p.Unit = req.Unit;
            p.MeasureId = req.MeasureId; p.PackageCode = req.PackageCode; p.PackageName = req.PackageName;
            p.PackageQty = req.PackageQty;
            p.VatRate = req.VatRate; p.ExciseRate = req.ExciseRate; p.BasePrice = req.BasePrice;
            p.Currency = string.IsNullOrWhiteSpace(req.Currency) ? "UZS" : req.Currency.ToUpperInvariant();
            p.IsPriceFixed = req.IsPriceFixed;

            if (req.Id is null) db.Products.Add(p);
            audit.Write(actor.Id, nameof(Product), req.Id, req.Id is null ? "create" : "update");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { p.Id });
        });

        /// Katalogni ommaviy yuklash (Excel'dan CSV qilib).
        pr.MapPost("/bulk", async (List<UpsertProduct> rows, CurrentUser cu, AppDbContext db,
                                   CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (actor.Role is not (UserRole.Admin or UserRole.Realizatsiya or UserRole.Perehod))
                throw new BusinessException("FORBIDDEN", "Ruxsat yo'q");

            var created = 0; var updated = 0;
            foreach (var r in rows)
            {
                var existing = r.Article is null ? null
                    : await db.Products.FirstOrDefaultAsync(p => p.Article == r.Article, ct);

                if (existing is null)
                {
                    db.Products.Add(new Product
                    {
                        GroupId = r.GroupId, Article = r.Article, Name = r.Name, Brand = r.Brand,
                        Barcode = r.Barcode, Mxik = r.Mxik, MxikName = r.MxikName, Unit = r.Unit,
                        MeasureId = r.MeasureId, PackageCode = r.PackageCode, PackageName = r.PackageName,
                        PackageQty = r.PackageQty, VatRate = r.VatRate, ExciseRate = r.ExciseRate,
                        BasePrice = r.BasePrice,
                        Currency = string.IsNullOrWhiteSpace(r.Currency) ? "UZS" : r.Currency.ToUpperInvariant(),
                        IsPriceFixed = r.IsPriceFixed
                    });
                    created++;
                }
                else
                {
                    existing.Name = r.Name; existing.BasePrice = r.BasePrice;
                    existing.Currency = string.IsNullOrWhiteSpace(r.Currency) ? existing.Currency : r.Currency.ToUpperInvariant();
                    existing.Mxik = r.Mxik ?? existing.Mxik;
                    existing.Unit = r.Unit; existing.GroupId = r.GroupId ?? existing.GroupId;
                    updated++;
                }
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { created, updated });
        });
    }
}

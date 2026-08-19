using Bigant.Api.Auth;
using Bigant.Didox;
using Bigant.Infrastructure.Notifications;
using Microsoft.Extensions.Options;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

/// <summary>
/// Ma'lumotnomalar: Bigant rekvizitlari, xodimlar, avtomobillar,
/// shartnoma shablonlari. Bularsiz Didox hujjatlari shakllanmaydi.
/// </summary>
public static class DirectoryEndpoints
{
    public sealed record UpsertEmployee(long? Id, string FullName, string? Position,
        string? Pinfl, string? PassportSeries, string? PassportNumber, string? PassportIssuedBy,
        DateOnly? PassportIssuedDate, string? Phone, bool IsDriver, string? DriverLicense);

    public sealed record UpsertVehicle(long? Id, string Model, string PlateNumber,
        string? TrailerNumber, decimal? CapacityTons, string? CarrierName, string? CarrierInn);

    public sealed record TemplatePart(int OrdNo, string Title, string Body);
    public sealed record UpsertTemplate(long? Id, string Name, bool? ForSale, bool IsDefault,
        List<TemplatePart> Parts);

    public static void MapDirectoryEndpoints(this WebApplication app)
    {
        // ══════════ BIGANT REKVIZITLARI ══════════
        var co = app.MapGroup("/api/company").WithTags("Sozlamalar").RequireAuthorization().RequireRateLimiting("api");

        co.MapGet("", async (AppDbContext db, CancellationToken ct) =>
        {
            var p = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
            if (p is null)
                return Results.Ok(new { exists = false, missing = new[] { "Rekvizitlar umuman kiritilmagan" } });

            return Results.Ok(new
            {
                exists = true,
                p.Id, p.Name, p.Tin, p.BranchCode, p.BranchName,
                p.Account, p.BankId, p.BankName,
                p.Address, p.RegionId, p.RegionName, p.DistrictCode, p.DistrictName,
                p.Oked, p.DirectorName, p.DirectorPinfl, p.AccountantName, p.AccountantPinfl,
                p.VatRegCode, p.VatRegStatus, p.TaxpayerType,
                p.WorkPhone, p.Mobile, p.Email, p.WarehouseAddress,
                readyForDidox = p.IsReadyForDidox,
                missing = p.MissingForDidox().ToArray()
            });
        });

        co.MapPost("", async (CompanyProfile req, CurrentUser cu, AppDbContext db,
                              IAuditService audit, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (actor.Role != UserRole.Admin)
                throw new BusinessException("FORBIDDEN", "Rekvizitlarni faqat rahbar o'zgartiradi");

            var p = await db.CompanyProfiles.FirstOrDefaultAsync(ct);
            if (p is null) { db.CompanyProfiles.Add(req); p = req; }
            else
            {
                p.Name = req.Name; p.Tin = req.Tin;
                p.BranchCode = req.BranchCode; p.BranchName = req.BranchName;
                p.Account = req.Account; p.BankId = req.BankId; p.BankName = req.BankName;
                p.Address = req.Address;
                p.RegionId = req.RegionId; p.RegionName = req.RegionName;
                p.DistrictCode = req.DistrictCode; p.DistrictName = req.DistrictName;
                p.Oked = req.Oked;
                p.DirectorName = req.DirectorName; p.DirectorPinfl = req.DirectorPinfl;
                p.AccountantName = req.AccountantName; p.AccountantPinfl = req.AccountantPinfl;
                p.VatRegCode = req.VatRegCode; p.VatRegStatus = req.VatRegStatus;
                p.TaxpayerType = req.TaxpayerType;
                p.WorkPhone = req.WorkPhone; p.Mobile = req.Mobile; p.Email = req.Email;
                p.WarehouseAddress = req.WarehouseAddress;
            }

            audit.Write(actor.Id, nameof(CompanyProfile), p.Id, "update");
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                p.Id, readyForDidox = p.IsReadyForDidox, missing = p.MissingForDidox().ToArray()
            });
        });

        // ══════════ XODIMLAR (doverennost / haydovchi) ══════════
        var em = app.MapGroup("/api/employees").WithTags("Ma'lumotnoma").RequireAuthorization().RequireRateLimiting("api");

        em.MapGet("", async (bool? driversOnly, AppDbContext db, CancellationToken ct) =>
        {
            var q = db.Employees.AsNoTracking().Where(e => e.IsActive);
            if (driversOnly == true) q = q.Where(e => e.IsDriver);

            var rows = await q.OrderBy(e => e.FullName).ToListAsync(ct);
            return Results.Ok(rows.Select(e => new
            {
                e.Id, e.FullName, e.Position, e.Pinfl, e.Phone, e.IsDriver, e.DriverLicense,
                canReceiveGoods = e.CanReceiveGoods,
                problem = e.CanReceiveGoods ? null
                    : "PINFL (14 raqam) yo'q — doverennost chiqarib bo'lmaydi"
            }));
        });

        em.MapPost("", async (UpsertEmployee req, CurrentUser cu, AppDbContext db, CancellationToken ct) =>
        {
            // Ma'lumotnomani faqat ADMIN o'zgartiradi — buxgalterlar ko'radi, tegmaydi.
            var actor = await cu.GetAsync(ct);
            if (actor.Role != UserRole.Admin)
                throw new BusinessException("FORBIDDEN", "Bu bo'limni faqat administrator o'zgartiradi");

            if (!string.IsNullOrWhiteSpace(req.Pinfl) && req.Pinfl.Length != 14)
                throw new BusinessException("BAD_PINFL", "PINFL 14 ta raqamdan iborat bo'lishi kerak");

            var e = req.Id is { } id
                ? await db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct)
                  ?? throw new BusinessException("NOT_FOUND", "Xodim topilmadi")
                : new Employee();

            e.FullName = req.FullName; e.Position = req.Position; e.Pinfl = req.Pinfl;
            e.PassportSeries = req.PassportSeries; e.PassportNumber = req.PassportNumber;
            e.PassportIssuedBy = req.PassportIssuedBy; e.PassportIssuedDate = req.PassportIssuedDate;
            e.Phone = req.Phone; e.IsDriver = req.IsDriver; e.DriverLicense = req.DriverLicense;

            if (req.Id is null) db.Employees.Add(e);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { e.Id, canReceiveGoods = e.CanReceiveGoods });
        });

        // ══════════ AVTOMOBILLAR (TTN) ══════════
        var ve = app.MapGroup("/api/vehicles").WithTags("Ma'lumotnoma").RequireAuthorization().RequireRateLimiting("api");

        ve.MapGet("", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Vehicles.AsNoTracking().Where(v => v.IsActive)
                .OrderBy(v => v.Model)
                .Select(v => new { v.Id, v.Model, v.PlateNumber, v.TrailerNumber,
                                   v.CapacityTons, v.CarrierName })
                .ToListAsync(ct)));

        ve.MapPost("", async (UpsertVehicle req, CurrentUser cu, AppDbContext db, CancellationToken ct) =>
        {
            // Ma'lumotnomani faqat ADMIN o'zgartiradi — buxgalterlar ko'radi, tegmaydi.
            var actor = await cu.GetAsync(ct);
            if (actor.Role != UserRole.Admin)
                throw new BusinessException("FORBIDDEN", "Bu bo'limni faqat administrator o'zgartiradi");

            var v = req.Id is { } id
                ? await db.Vehicles.FirstOrDefaultAsync(x => x.Id == id, ct)
                  ?? throw new BusinessException("NOT_FOUND", "Avtomobil topilmadi")
                : new Vehicle();

            v.Model = req.Model; v.PlateNumber = req.PlateNumber;
            v.TrailerNumber = req.TrailerNumber; v.CapacityTons = req.CapacityTons;
            v.CarrierName = req.CarrierName; v.CarrierInn = req.CarrierInn;

            if (req.Id is null) db.Vehicles.Add(v);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { v.Id });
        });

        // ══════════ SHARTNOMA SHABLONLARI (Didox Parts[]) ══════════
        var tp = app.MapGroup("/api/contract-templates").WithTags("Ma'lumotnoma").RequireAuthorization().RequireRateLimiting("api");

        tp.MapGet("", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ContractTemplates.AsNoTracking()
                .Where(t => t.IsActive)
                .Select(t => new
                {
                    t.Id, t.Name, t.ForSale, t.IsDefault,
                    parts = t.Parts.OrderBy(p => p.OrdNo)
                                   .Select(p => new { p.OrdNo, p.Title, p.Body })
                })
                .ToListAsync(ct)));

        tp.MapPost("", async (UpsertTemplate req, CurrentUser cu, AppDbContext db, CancellationToken ct) =>
        {
            // Ma'lumotnomani faqat ADMIN o'zgartiradi — buxgalterlar ko'radi, tegmaydi.
            var actor = await cu.GetAsync(ct);
            if (actor.Role != UserRole.Admin)
                throw new BusinessException("FORBIDDEN", "Bu bo'limni faqat administrator o'zgartiradi");

            if (req.Parts is null or { Count: 0 })
                throw new BusinessException("NO_PARTS",
                    "Shablonda kamida bitta bo'lim bo'lishi kerak — Didox bo'limsiz dogovorni qabul qilmaydi");

            var t = req.Id is { } id
                ? await db.ContractTemplates.Include(x => x.Parts)
                          .FirstOrDefaultAsync(x => x.Id == id, ct)
                  ?? throw new BusinessException("NOT_FOUND", "Shablon topilmadi")
                : new ContractTemplate();

            t.Name = req.Name; t.ForSale = req.ForSale; t.IsDefault = req.IsDefault;
            t.Parts.Clear();
            t.Parts.AddRange(req.Parts.Select(p => new ContractTemplatePart
            {
                OrdNo = p.OrdNo, Title = p.Title, Body = p.Body
            }));

            if (req.Id is null) db.ContractTemplates.Add(t);

            if (t.IsDefault)
                await db.ContractTemplates
                    .Where(x => x.Id != t.Id && x.ForSale == t.ForSale)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { t.Id, parts = t.Parts.Count });
        });

        // ══════════════════════════════════════════════════════════
        //  DIDOX'NI YOQISHDAN OLDIN
        //
        //  Faqat HAQIQATAN to'suvchi narsalar. Ilgari bu yerda direktor
        //  PINFL, hisob raqami, ishonchli shaxs, haydovchi va shartnoma
        //  shabloni ham tekshirilardi — ular BIZ hujjat yaratgan davrdan
        //  qolgan edi. Endi hujjatni Didox yaratadi, bu ma'lumotlar Didox
        //  tomonida turadi va bizda hech qayerda o'qilmaydi. Ularni talab
        //  qilish 93 kontragentdan 89 tasini «nosoz» deb ko'rsatib,
        //  bugalterga ma'nosiz ogohlantirish berardi.
        //
        //  XAVFSIZLIK: RequireAuthorization tushib qolgan edi — tokensiz ham
        //  200 qaytarardi (2026-08-19 auditda topilgan).
        // ══════════════════════════════════════════════════════════
        app.MapGet("/api/didox-readiness", async (AppDbContext db, IOptions<DidoxOptions> didox,
                                                  IOptions<TelegramOptions> tg, CancellationToken ct) =>
        {
            var company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
            var d = didox.Value;

            // Kontragentni TANIB OLISH uchun INN kerak. Busiz keyingi hujjat
            // shu kontragentga bog'lanmaydi va qarz ikkiga bo'linib ketadi.
            var noInn = await db.Counterparties.AsNoTracking()
                .Where(c => !c.IsBlocked && c.Inn == null && c.Pinfl == null)
                .Select(c => new { c.Id, c.Name })
                .Take(10)
                .ToListAsync(ct);

            var cpTotal = await db.Counterparties.AsNoTracking().CountAsync(c => !c.IsBlocked, ct);

            return Results.Ok(new
            {
                company = new
                {
                    exists = company is not null,
                    // Didox uchun bizdan yagona zarur narsa — O'Z INN imiz:
                    // hujjat bizga kelganmi yoki biz yuborganmizmi, shu bilan aniqlanadi.
                    inn = company?.Tin,
                    ready = !string.IsNullOrWhiteSpace(company?.Tin),
                },
                didox = new
                {
                    enabled = d.Enabled,
                    baseUrl = d.BaseUrl,
                    hasToken = !string.IsNullOrWhiteSpace(d.PartnerToken),
                    hasLogin = !string.IsNullOrWhiteSpace(d.Login) && !string.IsNullOrWhiteSpace(d.Password),
                },
                telegram = new
                {
                    enabled = tg.Value.Enabled,
                    connected = !string.IsNullOrWhiteSpace(company?.TelegramChatId),
                },
                counterparties = new
                {
                    total = cpTotal,
                    noInn = noInn.Count,
                    sample = noInn,
                },
            });
        }).RequireAuthorization();
    }
}

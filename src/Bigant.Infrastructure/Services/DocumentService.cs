using Bigant.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigant.Didox;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bigant.Infrastructure.Services;

/// <summary>Doverennost uchun qo'shimcha ma'lumot. Didox: Agent.Pinfl MAJBURIY.</summary>
public sealed record DoverennostData(
    long? EmployeeId,
    string? EmployeeName,
    string? EmployeePinfl,
    string? Position,
    DateOnly? ValidUntil,
    string? BasisDocument);

/// <summary>TTN uchun qo'shimcha ma'lumot. Haydovchi PINFL va mashina raqamisiz TTN chiqmaydi.</summary>
public sealed record TtnData(
    long? DriverId,
    string? DriverName,
    string? DriverPinfl,
    long? VehicleId,
    string? VehicleModel,
    string? VehicleNumber,
    string? TrailerNumber,
    string? LoadAddress,
    int? LoadRegionId,
    int? LoadDistrictCode,
    string? UnloadAddress,
    int? UnloadRegionId,
    int? UnloadDistrictCode,
    decimal? DistanceKm,
    decimal? WeightBrutto,
    decimal? WeightNetto,
    decimal? FreightCost);

public sealed record NewDocument(
    long DealId,
    DocType DocType,
    DoverennostData? Doverennost = null,
    TtnData? Ttn = null);

public interface IDocumentService
{
    /// <summary>Shartnoma kartochkasidan hujjat shakllantiradi (ma'lumot qayta kiritilmaydi).</summary>
    Task<Document> CreateFromDealAsync(NewDocument req, AppUser actor, CancellationToken ct = default);

    /// <summary>Bugalter imzoladi. Didox o'chiq bo'lsa pkcs7 shart emas.</summary>
    Task<Document> MarkSignedAsync(long documentId, string? pkcs7, AppUser actor, CancellationToken ct = default);

    /// <summary>Imzolangan hujjatni chiquvchi navbatga qo'yadi.</summary>
    Task<Document> EnqueueSendAsync(long documentId, AppUser actor, CancellationToken ct = default);

    /// <summary>Bir necha hujjatni bittada imzolab yuborish (bugalterning kunlik ishi).</summary>
    Task<int> SignAndSendBatchAsync(IEnumerable<long> documentIds, string? pkcs7, AppUser actor,
                                    CancellationToken ct = default);
}

public sealed class DocumentService : IDocumentService
{
    private readonly AppDbContext _db;
    private readonly INumberGenerator _numbers;
    private readonly IDidoxClient _didox;
    private readonly IDealService _deals;
    private readonly IAuditService _audit;
    private readonly INotificationService _notify;
    private readonly ILogger<DocumentService> _log;

    public DocumentService(AppDbContext db, INumberGenerator numbers, IDidoxClient didox,
                           IDealService deals, IAuditService audit,
                           INotificationService notify, ILogger<DocumentService> log)
    {
        _db = db; _numbers = numbers; _didox = didox; _deals = deals;
        _audit = audit; _notify = notify; _log = log;
    }

    // ══════════════════════════════════════════════════════════════
    public async Task<Document> CreateFromDealAsync(NewDocument req, AppUser actor,
        CancellationToken ct = default)
    {
        var (dealId, docType) = (req.DealId, req.DocType);

        var deal = await _db.Deals
            .Include(d => d.Counterparty)
            .Include(d => d.Items).ThenInclude(i => i.Product)
            .Include(d => d.Documents)
            .Include(d => d.Parts)
            .FirstOrDefaultAsync(d => d.Id == dealId, ct)
            ?? throw new BusinessException("NO_DEAL", "Shartnoma topilmadi");

        if (deal.Status == DealStatus.Cancelled)
            throw new BusinessException("CANCELLED", "Bekor qilingan shartnomaga hujjat chiqarilmaydi");

        // Bitta shartnomada bitta turdagi faol hujjat — dublikatning oldini oladi.
        var existing = deal.Documents.FirstOrDefault(d =>
            d.DocType == docType && d.Status != DocStatus.Cancelled && d.Status != DocStatus.Rejected);
        if (existing is not null)
            throw new BusinessException("DUPLICATE",
                $"{DealWorkflow.DocLabel(docType)} allaqachon mavjud: №{existing.DocNumber}");

        // Kim chiqaradi — DealWorkflow hal qiladi (doverennost teskari!).
        var direction = DealWorkflow.DirectionFor(deal.Kind, docType, deal.ContractByUs, deal.TtnByUs);
        if (direction == DocDirection.In)
            throw new BusinessException("INCOMING_DOC",
                $"{DealWorkflow.DocLabel(docType)} ni qarshi tomon chiqaradi. " +
                "Kelganda /api/inbox orqali ro'yxatga olinadi (yoki Didox o'zi tortadi).");

        var prefix = docType switch
        {
            DocType.Esf => "ESF",
            DocType.Ttn => "TTN",
            DocType.Doverennost => "DOV",
            DocType.InvoiceForPayment => "SCH",
            DocType.Contract => "DOG",
            _ => "DOC"
        };

        // Bigant rekvizitlari — Didox har bir hujjatda talab qiladi.
        var us = await _db.CompanyProfiles.FirstOrDefaultAsync(ct)
                 ?? throw new BusinessException("NO_COMPANY_PROFILE",
                     "Bigant rekvizitlari kiritilmagan. Sozlamalar → Kompaniya bo'limini to'ldiring.");

        var missing = us.MissingForDidox().ToList();
        if (missing.Count > 0 && _didox.IsEnabled)
            throw new BusinessException("COMPANY_INCOMPLETE",
                "Kompaniya rekvizitlari to'liq emas: " + string.Join(", ", missing));

        var doc = new Document
        {
            DealId = deal.Id,
            DocType = docType,
            Direction = direction,
            Status = DocStatus.ReadyToSign,
            DocNumber = await _numbers.NextAsync(prefix, ct),
            DocDate = UzTime.Today,
            CounterpartyId = deal.CounterpartyId,
            Amount = deal.AmountTotal,
            VatAmount = deal.Items.Sum(i => i.VatAmount),
            // Zanjir: bu hujjat qaysi hujjat asosida chiqarilgan.
            // «Aynan shu dogovor uchun qaysi doverennost» savoliga javob.
            ParentDocumentId = DealWorkflow.ResolveParent(
                docType,
                deal.Documents
                    .Where(d => d.Status != DocStatus.Cancelled && d.Status != DocStatus.Rejected)
                    .Select(d => (d.DocType, d.Id))),
            Payload = JsonDocument.Parse("{}")
        };

        // ── Turga xos qo'shimcha ma'lumot ──
        DoverennostInfo? dov = null;
        if (docType == DocType.Doverennost)
        {
            dov = await BuildDoverennostAsync(req.Doverennost, deal, ct);
            doc.Doverennost = dov;
        }
        else if (docType == DocType.Ttn)
        {
            doc.Ttn = await BuildTtnAsync(req.Ttn, deal, us, ct);
        }

        doc.Payload = BuildPayloadSafe(doc, deal, us, dov);

        _db.Documents.Add(doc);

        // Hujjat chiqarilishi — bitim harakatga tushgani demak.
        if (deal.Status == DealStatus.Draft) deal.Status = DealStatus.Active;

        _audit.Write(actor.Id, nameof(Document), null, "create",
            null, new { deal.Number, Type = docType.ToString(), doc.DocNumber });
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Hujjat shakllandi {Type} {Number} (shartnoma {Deal})",
            docType, doc.DocNumber, deal.Number);
        return doc;
    }

    // ══════════════════════════════════════════════════════════════
    public async Task<Document> MarkSignedAsync(long documentId, string? pkcs7, AppUser actor,
        CancellationToken ct = default)
    {
        if (actor.Role is not (UserRole.Accountant or UserRole.Manager or UserRole.Admin))
            throw new BusinessException("FORBIDDEN", "Hujjatni faqat bugalter yoki rahbar imzolaydi");

        // Doverennost muddati xabarda ko'rsatiladi — shuning uchun yuklaymiz.
        var doc = await _db.Documents
                      .Include(d => d.Doverennost)
                      .FirstOrDefaultAsync(d => d.Id == documentId, ct)
                  ?? throw new BusinessException("NO_DOC", "Hujjat topilmadi");

        if (!doc.IsSignable)
            throw new BusinessException("BAD_STATUS", $"Hujjat holati {doc.Status} — imzolab bo'lmaydi");

        // Didox yoqilgan bo'lsa E-IMZO imzosi majburiy.
        if (_didox.IsEnabled && string.IsNullOrWhiteSpace(pkcs7))
            throw new BusinessException("NO_SIGNATURE",
                "Didox yoqilgan — hujjat E-IMZO bilan imzolanishi shart");

        doc.SignedData = pkcs7;
        doc.SignedByUserId = actor.Id;
        doc.SignedAt = DateTimeOffset.UtcNow;
        doc.Status = DocStatus.Signed;
        doc.UpdatedAt = DateTimeOffset.UtcNow;

        // Cheklistdagi mos qadam shu yerda yopiladi.
        // Didox yoqilgan bo'lsa yuborish jobi ham yopadi — u faqat Pending qadamga tegadi,
        // shuning uchun ikki marta yopilib qolmaydi.
        if (doc.DealId is { } dealId &&
            DealWorkflow.StepForDocument(doc.DocType) is { } stepType)
        {
            var step = await _db.DealSteps
                .FirstOrDefaultAsync(s => s.DealId == dealId && s.Step == stepType, ct);
            if (step is { Status: StepStatus.Pending })
            {
                step.Status = StepStatus.Done;
                step.CompletedAt = DateTimeOffset.UtcNow;
                step.DocumentId = doc.Id;
            }
        }

        // ── «YUBORILDI» TASDIG'I ──
        // Egasining talabi: doverennost yuborilganda ham xabar kelsin, faqat
        // yuborilmaganda emas. Shunda bugalter ish bajarilganini ko'radi.
        var dealInfo = doc.DealId is { } did
            ? await _db.Deals.AsNoTracking()
                .Where(d => d.Id == did)
                .Select(d => new { d.Number, Cp = d.Counterparty!.Name, d.ManagerId })
                .FirstOrDefaultAsync(ct)
            : null;

        if (dealInfo is not null)
        {
            await _notify.RaiseAsync(new Notification
            {
                Kind = NotificationKind.IncomingDocument,
                Severity = NotificationSeverity.Info,
                UserId = dealInfo.ManagerId,
                DealId = doc.DealId,
                DocumentId = doc.Id,
                Title = $"\U0001F4E4 {DealWorkflow.DocLabel(doc.DocType).ToUpperInvariant()} YUBORILDI",
                Body = $"Kimga:   {dealInfo.Cp}\n" +
                       $"Hujjat:  №{doc.DocNumber}\n" +
                       (doc.Amount > 0 ? $"Summa:   {doc.Amount:N0} so'm\n" : "") +
                       (doc.Doverennost?.ValidUntil is { } vu
                            ? $"\n⏳ Amal qiladi: {vu:dd.MM.yyyy} gacha\n" : "") +
                       $"\nShartnoma: {dealInfo.Number}",
                DedupKey = $"Sent:doc:{doc.Id}"
            }, ct);
        }

        _audit.Write(actor.Id, nameof(Document), doc.Id, "sign");
        await _db.SaveChangesAsync(ct);
        return doc;
    }

    // ══════════════════════════════════════════════════════════════
    public async Task<Document> EnqueueSendAsync(long documentId, AppUser actor, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct)
                  ?? throw new BusinessException("NO_DOC", "Hujjat topilmadi");

        if (!doc.IsSendable)
            throw new BusinessException("NOT_SIGNED", "Avval hujjatni imzolang");

        // Idempotentlik: bitta hujjat uchun bitta navbat yozuvi.
        var already = await _db.Outbox.AnyAsync(o => o.DocumentId == doc.Id && o.SentAt == null, ct);
        if (!already)
        {
            _db.Outbox.Add(new OutboxItem { DocumentId = doc.Id });
            _audit.Write(actor.Id, nameof(Document), doc.Id, "enqueue");
            await _db.SaveChangesAsync(ct);
        }

        return doc;
    }

    public async Task<int> SignAndSendBatchAsync(IEnumerable<long> documentIds, string? pkcs7,
        AppUser actor, CancellationToken ct = default)
    {
        var n = 0;
        foreach (var id in documentIds.Distinct())
        {
            try
            {
                await MarkSignedAsync(id, pkcs7, actor, ct);
                await EnqueueSendAsync(id, actor, ct);
                n++;
            }
            catch (BusinessException ex)
            {
                _log.LogWarning("Hujjat {Id} o'tkazib yuborildi: {Msg}", id, ex.Message);
            }
        }
        return n;
    }

    // ══════════════════════════════════════════════════════════════
    //  TURGA XOS MA'LUMOTLAR
    // ══════════════════════════════════════════════════════════════
    private async Task<DoverennostInfo> BuildDoverennostAsync(
        DoverennostData? data, Deal deal, CancellationToken ct)
    {
        if (data is null)
            throw new BusinessException("NO_DOVERENNOST_DATA",
                "Doverennost uchun ishonchli shaxs ko'rsatilmagan");

        Employee? emp = data.EmployeeId is { } id
            ? await _db.Employees.FirstOrDefaultAsync(e => e.Id == id && e.IsActive, ct)
              ?? throw new BusinessException("NO_EMPLOYEE", "Xodim topilmadi")
            : null;

        var name  = data.EmployeeName  ?? emp?.FullName;
        var pinfl = data.EmployeePinfl ?? emp?.Pinfl;

        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessException("NO_AGENT_NAME", "Ishonchli shaxs F.I.Sh kiritilmagan");
        if (string.IsNullOrWhiteSpace(pinfl) || pinfl.Length != 14)
            throw new BusinessException("NO_AGENT_PINFL",
                "Ishonchli shaxs PINFL (14 raqam) kiritilmagan — Didox buni majburiy talab qiladi");

        return new DoverennostInfo
        {
            EmployeeId       = emp?.Id,
            EmployeeName     = name,
            EmployeePinfl    = pinfl,
            Position         = data.Position ?? emp?.Position,
            PassportSeries   = emp?.PassportSeries,
            PassportNumber   = emp?.PassportNumber,
            PassportIssuedBy = emp?.PassportIssuedBy,
            PassportIssuedDate = emp?.PassportIssuedDate,
            SupplierName     = deal.Counterparty?.Name,
            BasisDocument    = data.BasisDocument ?? deal.Number,
            IssuedOn         = UzTime.Today,
            // Standart 10 kun — O'zbekistonda odatiy muddat
            ValidUntil       = data.ValidUntil ?? UzTime.Today.AddDays(10)
        };
    }

    private async Task<TtnInfo> BuildTtnAsync(
        TtnData? data, Deal deal, CompanyProfile us, CancellationToken ct)
    {
        if (data is null)
            throw new BusinessException("NO_TTN_DATA",
                "TTN uchun haydovchi va avtomobil ko'rsatilmagan");

        Employee? drv = data.DriverId is { } did
            ? await _db.Employees.FirstOrDefaultAsync(e => e.Id == did && e.IsActive, ct)
            : null;
        Vehicle? veh = data.VehicleId is { } vid
            ? await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == vid && v.IsActive, ct)
            : null;

        var driverName  = data.DriverName  ?? drv?.FullName;
        var driverPinfl = data.DriverPinfl ?? drv?.Pinfl;
        var model       = data.VehicleModel  ?? veh?.Model;
        var plate       = data.VehicleNumber ?? veh?.PlateNumber;

        if (string.IsNullOrWhiteSpace(driverName))
            throw new BusinessException("NO_DRIVER", "Haydovchi F.I.Sh kiritilmagan");
        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(plate))
            throw new BusinessException("NO_VEHICLE",
                "Avtomobil rusumi va davlat raqami kiritilmagan");

        var isSale = deal.Kind == DealKind.Sale;
        var cp = deal.Counterparty;

        return new TtnInfo
        {
            DriverId = drv?.Id,
            DriverName = driverName,
            DriverPinfl = driverPinfl,
            DriverLicense = drv?.DriverLicense,
            VehicleId = veh?.Id,
            VehicleModel = model,
            VehicleNumber = plate,
            TrailerNumber = data.TrailerNumber ?? veh?.TrailerNumber,

            // Sotuvda bizdan klientga, xaridda postavshikdan bizga
            LoadAddress   = data.LoadAddress
                            ?? (isSale ? us.WarehouseAddress ?? us.Address : cp?.Address) ?? "—",
            LoadRegionId  = data.LoadRegionId  ?? (isSale ? us.RegionId : cp?.RegionId),
            LoadDistrictCode = data.LoadDistrictCode ?? (isSale ? us.DistrictCode : cp?.DistrictCode),

            UnloadAddress = data.UnloadAddress
                            ?? (isSale ? deal.DeliveryAddress ?? cp?.Address : us.WarehouseAddress ?? us.Address) ?? "—",
            UnloadRegionId = data.UnloadRegionId ?? (isSale ? cp?.RegionId : us.RegionId),
            UnloadDistrictCode = data.UnloadDistrictCode ?? (isSale ? cp?.DistrictCode : us.DistrictCode),

            ShipDate = UzTime.Today,
            DistanceKm = data.DistanceKm,
            WeightBrutto = data.WeightBrutto,
            WeightNetto = data.WeightNetto,
            FreightCost = data.FreightCost,
            TransportType = 1,
            CarrierName = veh?.CarrierName ?? us.Name,
            CarrierInn = veh?.CarrierInn ?? us.Tin,
            ResponsibleName = drv?.FullName ?? driverName,
            ResponsiblePinfl = driverPinfl
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  DIDOX PAYLOAD — rasmiy sxema bo'yicha (DidoxPayloadBuilder).
    //  Didox o'chiq bo'lsa ham to'liq to'ldiriladi, yoqilganda
    //  hech narsani qayta kiritish kerak bo'lmaydi.
    // ══════════════════════════════════════════════════════════════
    private static readonly JsonSerializerOptions PayloadJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,   // null obyektlar ATAYLAB yuboriladi
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Payloadni quradi. Didox maydoni yetishmasa:
    ///   • Didox YOQILGAN  → hujjat chiqarilmaydi, aniq xato beriladi
    ///   • Didox O'CHIQ    → hujjat chiqadi, payloadga "nima yetishmayapti" belgisi qo'yiladi
    /// Shu tariqa tizim Didox'siz to'xtamaydi, lekin qarz yashirin qolmaydi.
    /// </summary>
    private JsonDocument BuildPayloadSafe(
        Document doc, Deal deal, CompanyProfile us, DoverennostInfo? dov)
    {
        try
        {
            return BuildPayload(doc, deal, us, dov);
        }
        catch (InvalidOperationException ex)
        {
            if (_didox.IsEnabled)
                throw new BusinessException("DIDOX_FIELD_MISSING", ex.Message);

            _log.LogWarning("Didox maydoni yetishmayapti ({Doc}): {Msg}", doc.DocNumber, ex.Message);

            return JsonSerializer.SerializeToDocument(new
            {
                _didoxIncomplete = true,
                _missing = ex.Message,
                _hint = "Didox yoqilishidan oldin shu maydonni to'ldiring",
                documentType = doc.DocType.ToString(),
                docNo = doc.DocNumber,
                docDate = DidoxFormat.Date(doc.DocDate),
                contractNo = deal.Number,
                counterpartyInn = deal.Counterparty?.Inn,
                counterpartyName = deal.Counterparty?.Name,
                totalWithVat = DidoxFormat.Sum(deal.Items.Sum(i => i.Amount))
            }, PayloadJson);
        }
    }

    private static JsonDocument BuildPayload(
        Document doc, Deal deal, CompanyProfile us, DoverennostInfo? dov)
    {
        var other = deal.Counterparty;

        object body = doc.DocType switch
        {
            DocType.Doverennost => DidoxPayloadBuilder.BuildEmpowerment(
                doc, deal, us, other,
                dov ?? throw new BusinessException("NO_DOVERENNOST_DATA",
                    "Doverennost uchun ishonchli shaxs ma'lumotlari kiritilmagan")),

            DocType.Contract => DidoxPayloadBuilder.BuildContract(deal, us, other),

            DocType.Esf => DidoxPayloadBuilder.BuildFactura(doc, deal, us, other),

            // Schyot na oplatu — Didox'ning rasmiy turi emas, ichki hujjat.
            _ => new
            {
                documentType = doc.DocType.ToString(),
                docNo = doc.DocNumber,
                docDate = DidoxFormat.Date(doc.DocDate),
                contractNo = deal.Number,
                counterpartyInn = other.Inn,
                counterpartyName = other.Name,
                items = deal.Items.Select((i, n) => new
                {
                    ordNo = n + 1,
                    name = i.NameSnapshot ?? i.Product?.Name,
                    count = DidoxFormat.Count(i.Qty),
                    summa = DidoxFormat.Sum(i.Price),
                    deliverySum = DidoxFormat.Sum(i.NetAmount),
                    vatRate = DidoxFormat.Sum(i.VatRate),
                    vatSum = DidoxFormat.Sum(i.VatAmount),
                    deliverySumWithVat = DidoxFormat.Sum(i.Amount)
                }),
                totalWithVat = DidoxFormat.Sum(deal.Items.Sum(i => i.Amount))
            }
        };

        return JsonSerializer.SerializeToDocument(body, PayloadJson);
    }
}

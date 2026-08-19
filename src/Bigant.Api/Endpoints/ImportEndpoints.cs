using System.Globalization;
using System.Text.RegularExpressions;
using Bigant.Api.Auth;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Services;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Api.Endpoints;

/// <summary>
/// Excel importi. Bigant prays-listlari quyidagi ustunlar bilan keladi:
///
///   № · Rasm fayli · Brend · Artikul · Nomi · Nomi (uz) · O'lcham ·
///   Upakovkada · Birlik · Narx, у.е. · Tavsif · Kategoriya · Varaq · Izoh
///
/// Ustun tartibi emas, SARLAVHA NOMI bo'yicha topiladi — fayl o'zgarsa ham ishlaydi.
/// Narx у.е. da: Currency = "USD", so'mga o'girish shartnoma yaratilganda,
/// joriy kurs bo'yicha (DealService).
/// </summary>
public static partial class ImportEndpoints
{
    public static void MapImportEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/import").WithTags("Import").RequireAuthorization()
                   .DisableAntiforgery();

        // ── TOVARLAR ──
        g.MapPost("/products", async (IFormFile file, bool? dryRun, string? currency,
                                      CurrentUser cu, AppDbContext db, CancellationToken ct) =>
        {
            var actor = await cu.GetAsync(ct);
            if (actor.Role is not (UserRole.Admin or UserRole.Realizatsiya or UserRole.Perehod))
                throw new BusinessException("FORBIDDEN", "Katalogni bugalter yoki rahbar yuklaydi");

            if (file is null || file.Length == 0)
                throw new BusinessException("NO_FILE", "Fayl tanlanmadi");
            if (file.Length > 30 * 1024 * 1024)
                throw new BusinessException("TOO_BIG", "Fayl 30 MB dan katta");

            await using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();

            var head = ReadHeader(ws);
            string? Col(params string[] names) =>
                names.Select(n => head.GetValueOrDefault(Norm(n))).FirstOrDefault(v => v is not null);

            // Sarlavhalarni topamiz (turli fayllarda biroz farq qilishi mumkin).
            var cName  = Col("Nomi", "Наименование", "Name");
            var cArt   = Col("Artikul", "Артикул", "SKU");
            var cBrand = Col("Brend", "Бренд", "Brand");
            var cSize  = Col("O'lcham", "Oʻlcham", "Размер");
            var cUnit  = Col("Birlik", "Ед", "Единица");
            var cPrice = Col("Narx, у.е.", "Narx", "Цена", "Price");
            var cPack  = Col("Upakovkada", "В упаковке");
            var cCat   = Col("Kategoriya", "Категория", "Category");

            if (cName is null || cPrice is null)
                throw new BusinessException("BAD_HEADER",
                    "«Nomi» yoki «Narx» ustuni topilmadi. Topilgan ustunlar: " +
                    string.Join(", ", head.Values));

            var cur = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.ToUpperInvariant();

            // Mavjud tovarlar — artikul bo'yicha yangilanadi, dublikat yaratilmaydi.
            var byArticle = await db.Products
                .Where(p => p.Article != null)
                .ToDictionaryAsync(p => p.Article!, p => p, StringComparer.OrdinalIgnoreCase, ct);

            var groups = await db.ProductGroups.ToDictionaryAsync(
                x => x.Name, x => x, StringComparer.OrdinalIgnoreCase, ct);

            int created = 0, updated = 0, skipped = 0;
            var problems = new List<string>();
            var rows = ws.RangeUsed()?.RowsUsed().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();

            foreach (var row in rows)
            {
                string Cell(string? col) =>
                    col is null ? "" : row.Cell(col).GetString().Trim();

                var name = Cell(cName);
                if (string.IsNullOrWhiteSpace(name)) { skipped++; continue; }

                var price = ParsePrice(Cell(cPrice));
                if (price is null)
                {
                    skipped++;
                    if (problems.Count < 20)
                        problems.Add($"{name}: narx o'qilmadi «{Cell(cPrice)}»");
                    continue;
                }

                // «Nomi» + «O'lcham» — ikkalasi birga to'liq nomni beradi.
                var size = Cell(cSize);
                var full = string.IsNullOrWhiteSpace(size) ? name : $"{name} {size}";
                if (full.Length > 400) full = full[..400];

                var article = Cell(cArt);
                var unit = NormalizeUnit(Cell(cUnit));

                ProductGroup? grp = null;
                var catName = Cell(cCat);
                if (!string.IsNullOrWhiteSpace(catName))
                {
                    if (!groups.TryGetValue(catName, out grp))
                    {
                        grp = new ProductGroup { Name = catName, VatRate = 12 };
                        db.ProductGroups.Add(grp);
                        groups[catName] = grp;
                    }
                }

                if (!string.IsNullOrWhiteSpace(article) && byArticle.TryGetValue(article, out var ex))
                {
                    ex.Name = full;
                    ex.Brand = Cell(cBrand);
                    ex.Unit = unit;
                    ex.BasePrice = price.Value;
                    ex.Currency = cur;
                    ex.PackageQty = ParseNumber(Cell(cPack));
                    if (grp is not null) ex.Group = grp;
                    updated++;
                }
                else
                {
                    var p = new Product
                    {
                        Article = string.IsNullOrWhiteSpace(article) ? null : article,
                        Name = full,
                        Brand = Cell(cBrand),
                        Unit = unit,
                        BasePrice = price.Value,
                        Currency = cur,
                        PackageQty = ParseNumber(Cell(cPack)),
                        VatRate = 12,
                        Group = grp,
                        IsActive = true,
                    };
                    db.Products.Add(p);
                    if (p.Article is not null) byArticle[p.Article] = p;
                    created++;
                }
            }

            if (dryRun == true)
                return Results.Ok(new
                {
                    dryRun = true, created, updated, skipped,
                    problems,
                    note = "Sinov rejimi — hech narsa saqlanmadi"
                });

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                created, updated, skipped, currency = cur,
                problems,
                note = cur == "USD"
                    ? "Narxlar у.е. da saqlandi. Kurs kiritilmagan bo'lsa — Sozlamalar → Kurs"
                    : "Narxlar so'mda saqlandi"
            });
        });

        // ── KONTRAGENTLAR ──
        g.MapPost("/counterparties", async (IFormFile file, bool? dryRun,
                                            CurrentUser cu, AppDbContext db, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
                throw new BusinessException("NO_FILE", "Fayl tanlanmadi");

            await using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            var head = ReadHeader(ws);
            string? Col(params string[] n) =>
                n.Select(x => head.GetValueOrDefault(Norm(x))).FirstOrDefault(v => v is not null);

            var cName = Col("Nomi", "Naimenovanie", "Наименование", "Name");
            var cInn  = Col("INN", "ИНН", "Inn");
            var cKind = Col("Turi", "Тип", "Kind");
            var cAcc  = Col("Hisob raqami", "Расчетный счет", "Account");
            var cMfo  = Col("MFO", "МФО");
            var cDir  = Col("Direktor", "Директор");
            var cDays = Col("Otsrochka", "Отсрочка");

            if (cName is null || cInn is null)
                throw new BusinessException("BAD_HEADER",
                    "«Nomi» yoki «INN» ustuni topilmadi. Topilgan: " + string.Join(", ", head.Values));

            var byInn = await db.Counterparties.Where(c => c.Inn != null)
                .ToDictionaryAsync(c => c.Inn!, c => c, ct);

            int created = 0, updated = 0, skipped = 0;
            foreach (var row in ws.RangeUsed()!.RowsUsed().Skip(1))
            {
                string Cell(string? c) => c is null ? "" : row.Cell(c).GetString().Trim();

                var name = Cell(cName);
                var inn = Regex.Replace(Cell(cInn), @"\D", "");
                if (string.IsNullOrWhiteSpace(name) || inn.Length != 9) { skipped++; continue; }

                var kind = Cell(cKind).ToLowerInvariant();
                var isSupplier = kind.Contains("postav") || kind.Contains("постав") || kind.Contains("ta'min");
                var isClient = !isSupplier;

                if (byInn.TryGetValue(inn, out var ex))
                {
                    ex.Name = name;
                    ex.BankAccount = Cell(cAcc); ex.BankMfo = Cell(cMfo);
                    ex.DirectorName = Cell(cDir);
                    ex.PaymentDays = (int)(ParseNumber(Cell(cDays)) ?? ex.PaymentDays);
                    updated++;
                }
                else
                {
                    db.Counterparties.Add(new Counterparty
                    {
                        Name = name, Inn = inn,
                        IsClient = isClient, IsSupplier = isSupplier,
                        BankAccount = Cell(cAcc), BankMfo = Cell(cMfo),
                        DirectorName = Cell(cDir),
                        PaymentDays = (int)(ParseNumber(Cell(cDays)) ?? 0),
                    });
                    created++;
                }
            }

            if (dryRun == true)
                return Results.Ok(new { dryRun = true, created, updated, skipped });

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { created, updated, skipped });
        });
    }

    // ── Yordamchilar ────────────────────────────────────────────────
    /// <summary>Sarlavha qatorini o'qiydi: normallashtirilgan nom → ustun harfi.</summary>
    private static Dictionary<string, string> ReadHeader(IXLWorksheet ws)
    {
        var map = new Dictionary<string, string>();
        var first = ws.RangeUsed()?.FirstRowUsed();
        if (first is null) return map;

        foreach (var cell in first.Cells())
        {
            var text = cell.GetString().Trim();
            if (text.Length == 0) continue;
            map.TryAdd(Norm(text), cell.WorksheetColumn().ColumnLetter());
        }
        return map;
    }

    /// <summary>Sarlavhani solishtirish uchun soddalashtiradi.</summary>
    private static string Norm(string s) =>
        Regex.Replace(s.ToLowerInvariant(), @"[\s.,'’ʻ`_-]", "");

    /// <summary>
    /// «15,70» · «15.70» · «15,70 / 31,40» — oxirgi holatda BIRINCHISI olinadi
    /// (dona narxi; ikkinchisi juft narxi).
    /// </summary>
    private static decimal? ParsePrice(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var first = raw.Split('/')[0];
        return ParseNumber(first);
    }

    private static decimal? ParseNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var m = NumberRegex().Match(raw.Replace(',', '.').Replace(' ', ' '));
        if (!m.Success) return null;
        return decimal.TryParse(m.Value.Replace(" ", ""), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Prays-listdagi qisqartmalarni katalog birliklariga keltiradi.</summary>
    private static string NormalizeUnit(string raw)
    {
        var u = raw.Trim().ToLowerInvariant().TrimEnd('.');
        return u switch
        {
            "м" or "m" or "metr" or "метр" => "metr",
            "шт" or "dona" or "штука" or "pc" => "dona",
            "кг" or "kg" => "kg",
            "компл" or "komplekt" or "набор" => "komplekt",
            "упак" or "упаковка" or "quti" => "quti",
            "" => "dona",
            _ => u.Length > 32 ? u[..32] : u,
        };
    }

    [GeneratedRegex(@"-?\d[\d ]*(\.\d+)?")]
    private static partial Regex NumberRegex();
}

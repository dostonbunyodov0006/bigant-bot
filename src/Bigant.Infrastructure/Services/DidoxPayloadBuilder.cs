using Bigant.Domain;
using Bigant.Didox;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;

namespace Bigant.Infrastructure.Services;

/// <summary>
/// Bizning modelni Didox JSON sxemasiga o'giradi.
/// Bu yagona joy — boshqa hech qayerda Didox maydon nomlari yozilmasin.
/// </summary>
public static class DidoxPayloadBuilder
{
    // ═══════════════ DOVERENNOST ═══════════════
    /// <summary>
    /// DIQQAT — rasmiy hujjatdagi nomlash chalkash:
    /// eski sxemada "Seller" bloki XARIDOR (doverennost chiqaruvchi),
    /// "Buyer" bloki esa TOVAR BERUVCHI deb izohlangan.
    /// Yangi V2 sxemada Seller = tovar beruvchi, Buyer = xaridor.
    /// Bu yerda V2 mantiqi ishlatilgan — test stendda tasdiqlash shart.
    /// </summary>
    public static EmpowermentPayload BuildEmpowerment(
        Document doc, Deal deal, CompanyProfile us, Counterparty supplier, DoverennostInfo info)
    {
        var d = info;

        // Doverennostni doim XARIDOR chiqaradi.
        // Xaridda xaridor — biz; sotuvda — klient (u holda hujjat bizga keladi).
        var buyer = new EmpowermentParty(
            TinOrPinfl: DidoxFormat.Required(us.Tin, "Bigant INN"),
            Name:       DidoxFormat.Required(us.Name, "Bigant nomi"),
            BranchCode: us.BranchCode ?? "",
            BranchName: us.BranchName ?? "",
            Address:    DidoxFormat.Required(us.Address, "Bigant manzili"),
            Director:   new DidoxPerson(us.DirectorPinfl,
                            DidoxFormat.Required(us.DirectorName, "Bigant direktori")),
            Accountant: new DidoxPerson(us.AccountantPinfl, us.AccountantName));

        var seller = new EmpowermentParty(
            TinOrPinfl: DidoxFormat.Required(supplier.Inn ?? supplier.Pinfl, $"{supplier.Name} INN"),
            Name:       supplier.Name,
            BranchCode: supplier.BranchCode ?? "",
            BranchName: supplier.BranchName ?? "",
            Address:    supplier.Address,
            Director:   new DidoxPerson(supplier.DirectorPinfl, supplier.DirectorName),
            Accountant: new DidoxPerson(supplier.AccountantPinfl, supplier.AccountantName));

        var products = deal.Items.Select((it, i) => new EmpowermentProduct(
            OrdNo:       i + 1,
            Name:        it.NameSnapshot ?? it.Product?.Name ?? "—",
            CatalogCode: DidoxFormat.Text(it.MxikSnapshot),
            CatalogName: DidoxFormat.Text(it.Product?.MxikName),
            PackageCode: DidoxFormat.Text(it.PackageCode ?? it.Product?.PackageCode),
            PackageName: DidoxFormat.Text(it.UnitSnapshot ?? it.Product?.PackageName),
            Amount:      DidoxFormat.Count(it.Qty))).ToList();

        return new EmpowermentPayload(
            EmpowermentType: 0,
            EmpowermentDoc: new EmpowermentDoc(
                No:           DidoxFormat.Required(doc.DocNumber, "Doverennost raqami"),
                DateOfIssue:  DidoxFormat.Date(d.IssuedOn),
                DateOfExpire: DidoxFormat.Date(d.ValidUntil)),
            ContractDoc: new DidoxDocRef(
                No:   deal.Number,
                Date: DidoxFormat.Date(deal.ContractDate ?? doc.DocDate)),
            Seller:   seller,
            Buyer:    buyer,
            Agent:    new DidoxPerson(
                          DidoxFormat.Required(d.EmployeePinfl, "Ishonchli shaxs PINFL"),
                          DidoxFormat.Required(d.EmployeeName, "Ishonchli shaxs F.I.Sh")),
            Products: products);
    }

    // ═══════════════ DOGOVOR NK ═══════════════
    public static ContractPayload BuildContract(
        Deal deal, CompanyProfile us, Counterparty other)
    {
        if (deal.Parts.Count == 0)
            throw new BusinessException("NO_CONTRACT_PARTS",
                "Dogovor bo'limlari (Parts) bo'sh — Didox bunday shartnomani qabul qilmaydi. " +
                "Shartnoma shablonini tanlang.");

        var hasVat = deal.Items.Any(i => i.VatRate > 0);

        // Sotuvda biz — Owner (yetkazib beruvchi), xaridda — kontragent.
        var (owner, client) = deal.Kind == DealKind.Sale
            ? (UsAsParty(us), OtherAsParty(other))
            : (OtherAsParty(other), UsAsParty(us));

        var products = deal.Items.Select((it, i) =>
        {
            var withoutVat = it.VatRate <= 0;
            return new ContractProduct(
                OrdNo:              i + 1,
                Name:               it.NameSnapshot ?? it.Product?.Name ?? "—",
                CatalogCode:        DidoxFormat.Text(it.MxikSnapshot),
                CatalogName:        DidoxFormat.Text(it.Product?.MxikName),
                Barcode:            it.Product?.Barcode ?? "",
                MeasureId:          DidoxFormat.Text(it.UnitCodeSnapshot ?? it.Product?.MeasureId),
                PackageCode:        DidoxFormat.Text(it.PackageCode ?? it.Product?.PackageCode),
                PackageName:        DidoxFormat.Text(it.UnitSnapshot ?? it.Product?.PackageName),
                Count:              DidoxFormat.Count(it.Qty),
                Summa:              DidoxFormat.Sum(it.Price),
                DeliverySum:        DidoxFormat.Sum(it.NetAmount),
                VatRate:            DidoxFormat.Sum(it.VatRate),
                VatSum:             DidoxFormat.Sum(it.VatAmount),
                DeliverySumWithVat: DidoxFormat.Sum(it.Amount),
                WithoutVat:         withoutVat);
        }).ToList();

        var parts = deal.Parts.OrderBy(p => p.OrdNo)
            .Select(p => new ContractPartBlock(p.OrdNo, p.Title, p.Body)).ToList();

        return new ContractPayload(
            ContractDoc: new ContractDocBlock(
                ContractName:       DidoxFormat.Required(deal.ContractName, "Shartnoma nomi"),
                ContractNo:         deal.Number,
                ContractDate:       DidoxFormat.Date(deal.ContractDate ?? UzTime.Today),
                ContractExpireDate: DidoxFormat.Date(
                    deal.ContractExpireDate
                    ?? throw new BusinessException("NO_EXPIRE_DATE",
                        "Shartnoma tugash sanasi (ContractExpireDate) Didox uchun majburiy")),
                ContractPlace:      DidoxFormat.Required(deal.ContractPlace, "Shartnoma tuzilgan joyi")),
            Owner:    owner,
            Clients:  [client],
            Parts:    parts,
            Products: products,
            HasVat:   hasVat);
    }

    private static ContractParty UsAsParty(CompanyProfile us) => new(
        Tin:        DidoxFormat.Required(us.Tin, "Bigant INN"),
        Name:       DidoxFormat.Required(us.Name, "Bigant nomi"),
        BranchCode: us.BranchCode ?? "",
        BranchName: us.BranchName ?? "",
        FizTin:     DidoxFormat.Required(us.DirectorPinfl, "Bigant direktori PINFL"),
        Fio:        DidoxFormat.Required(us.DirectorName, "Bigant direktori F.I.Sh"),
        Address:    us.Address,
        WorkPhone:  us.WorkPhone ?? "",
        Mobile:     us.Mobile ?? "",
        Oked:       us.Oked,
        Account:    us.Account,
        BankId:     us.BankId);

    private static ContractParty OtherAsParty(Counterparty c) => new(
        Tin:        DidoxFormat.Required(c.Inn ?? c.Pinfl, $"{c.Name} INN"),
        Name:       c.Name,
        BranchCode: c.BranchCode ?? "",
        BranchName: c.BranchName ?? "",
        FizTin:     DidoxFormat.Required(c.DirectorPinfl, $"{c.Name} direktori PINFL"),
        Fio:        DidoxFormat.Required(c.DirectorName, $"{c.Name} direktori F.I.Sh"),
        Address:    c.Address,
        WorkPhone:  c.WorkPhone ?? "",
        Mobile:     c.Phone ?? "",
        Oked:       c.Oked,
        Account:    c.BankAccount,
        BankId:     c.BankMfo);

    // ═══════════════ SCHYOT-FAKTURA ═══════════════
    public static FacturaPayload BuildFactura(
        Document doc, Deal deal, CompanyProfile us, Counterparty other, int facturaType = FacturaType.Standard)
    {
        var hasVat    = deal.Items.Any(i => i.VatRate > 0);
        var hasExcise = deal.Items.Any(i => i.ExciseRate > 0);

        // Sotuvda sotuvchi — biz. Xaridda — kontragent.
        var isSale = deal.Kind == DealKind.Sale;

        var sellerTin = isSale
            ? DidoxFormat.Required(us.Tin, "Bigant INN")
            : DidoxFormat.Required(other.Inn, $"{other.Name} INN");
        var buyerTin = isSale
            ? DidoxFormat.Required(other.Inn, $"{other.Name} INN")
            : DidoxFormat.Required(us.Tin, "Bigant INN");

        var seller = isSale ? UsAsFacturaParty(us) : OtherAsFacturaParty(other);
        var buyer  = isSale ? OtherAsFacturaParty(other) : UsAsFacturaParty(us);

        var products = deal.Items.Select((it, i) => new FacturaProduct(
            OrdNo:              i + 1,
            Name:               it.NameSnapshot ?? it.Product?.Name ?? "—",
            CatalogCode:        DidoxFormat.Text(it.MxikSnapshot),
            CatalogName:        DidoxFormat.Text(it.Product?.MxikName),
            Barcode:            it.Product?.Barcode ?? "",
            PackageCode:        DidoxFormat.Text(it.PackageCode ?? it.Product?.PackageCode),
            PackageName:        DidoxFormat.Text(it.UnitSnapshot ?? it.Product?.PackageName),
            Count:              DidoxFormat.Count(it.Qty),
            Summa:              DidoxFormat.Sum(it.Price),
            DeliverySum:        DidoxFormat.Sum(it.NetAmount),
            VatRate:            DidoxFormat.Sum(it.VatRate),
            VatSum:             DidoxFormat.Sum(it.VatAmount),
            DeliverySumWithVat: DidoxFormat.Sum(it.Amount),
            // Qoida: WithoutVat faqat HasVat = true bo'lganda yuboriladi
            WithoutVat:         hasVat ? it.VatRate <= 0 : null)).ToList();

        var list = new FacturaProductList(
            Tin:       sellerTin,
            HasVat:    hasVat,
            HasExcise: hasExcise,
            Products:  products,
            TotalDeliverySum:        DidoxFormat.Sum(deal.Items.Sum(i => i.NetAmount)),
            TotalVatSum:             DidoxFormat.Sum(deal.Items.Sum(i => i.VatAmount)),
            TotalDeliverySumWithVat: DidoxFormat.Sum(deal.Items.Sum(i => i.Amount)));

        return new FacturaPayload(
            FacturaType: facturaType,
            FacturaDoc: new FacturaDocBlock(
                FacturaNo:   DidoxFormat.Required(doc.DocNumber, "Faktura raqami"),
                FacturaDate: DidoxFormat.Date(doc.DocDate)),
            ContractDoc: new FacturaContractBlock(
                ContractNo:   deal.Number,
                ContractDate: DidoxFormat.Date(deal.ContractDate ?? doc.DocDate)),
            SellerTin:   sellerTin,
            Seller:      seller,
            BuyerTin:    buyerTin,
            Buyer:       buyer,
            ProductList: list);
    }

    private static FacturaParty UsAsFacturaParty(CompanyProfile us) => new(
        Name:       DidoxFormat.Required(us.Name, "Bigant nomi"),
        BranchCode: us.BranchCode ?? "",
        BranchName: us.BranchName ?? "",
        Account:    us.Account,
        BankId:     us.BankId,
        Address:    us.Address,
        Director:   DidoxFormat.Required(us.DirectorName, "Bigant direktori (Didox majburiy)"),
        Accountant: us.AccountantName);

    private static FacturaParty OtherAsFacturaParty(Counterparty c) => new(
        Name:       c.Name,
        BranchCode: c.BranchCode ?? "",
        BranchName: c.BranchName ?? "",
        Account:    c.BankAccount,
        BankId:     c.BankMfo,
        Address:    c.Address,
        Director:   DidoxFormat.Required(c.DirectorName, $"{c.Name} direktori (Didox majburiy)"),
        Accountant: c.AccountantName);
}

using System.Text.Json.Serialization;

namespace Bigant.Didox;

// ═══════════════════════════════════════════════════════════════════
//  Didox JSON sxemalari — rasmiy hujjatdagi maydon nomlari bilan aynan.
//
//  MUHIM QOIDALAR (rasmiy hujjatdan):
//   1. Ortiqcha maydon TAQIQLANGAN — namunada yo'q narsa yuborilmaydi.
//   2. Ishlatilmagan obyekt butunlay null bo'ladi, bo'sh {} emas.
//   3. Raqamlar matn sifatida, nuqta bilan (DidoxFormat).
//   4. Sana faqat yyyy-MM-dd.
//
//  Shu sabab hamma joyda JsonIgnoreCondition.Never emas —
//  null obyektlar ATAYLAB yuboriladi.
// ═══════════════════════════════════════════════════════════════════

// ── Umumiy bloklar ────────────────────────────────────────────────

/// <summary>Mas'ul shaxs: PINFL + F.I.Sh.</summary>
public sealed record DidoxPerson(
    [property: JsonPropertyName("Pinfl")]    string? Pinfl,
    [property: JsonPropertyName("FullName")] string? FullName);

public sealed record DidoxDocRef(
    [property: JsonPropertyName("No")]   string No,
    [property: JsonPropertyName("Date")] string Date);

// ═══════════════════ 1. DOVERENNOST (yangi, V2) ═══════════════════
// Didox: "16. Доверенность (новая)"

public sealed record EmpowermentParty(
    [property: JsonPropertyName("TinOrPinfl")] string TinOrPinfl,
    [property: JsonPropertyName("Name")]       string Name,
    [property: JsonPropertyName("BranchCode")] string BranchCode,
    [property: JsonPropertyName("BranchName")] string BranchName,
    [property: JsonPropertyName("Address")]    string? Address,
    [property: JsonPropertyName("Director")]   DidoxPerson? Director,
    [property: JsonPropertyName("Accountant")] DidoxPerson? Accountant);

public sealed record EmpowermentProduct(
    [property: JsonPropertyName("OrdNo")]       int     OrdNo,
    [property: JsonPropertyName("Name")]        string  Name,
    [property: JsonPropertyName("CatalogCode")] string? CatalogCode,
    [property: JsonPropertyName("CatalogName")] string? CatalogName,
    [property: JsonPropertyName("PackageCode")] string? PackageCode,
    [property: JsonPropertyName("PackageName")] string? PackageName,
    [property: JsonPropertyName("Amount")]      string  Amount);

public sealed record EmpowermentPayload(
    [property: JsonPropertyName("EmpowermentType")]  int              EmpowermentType,
    [property: JsonPropertyName("EmpowermentDoc")]   EmpowermentDoc   EmpowermentDoc,
    [property: JsonPropertyName("ContractDoc")]      DidoxDocRef      ContractDoc,
    [property: JsonPropertyName("Seller")]           EmpowermentParty Seller,
    [property: JsonPropertyName("Buyer")]            EmpowermentParty Buyer,
    [property: JsonPropertyName("Agent")]            DidoxPerson      Agent,
    [property: JsonPropertyName("Products")]         IReadOnlyList<EmpowermentProduct> Products,
    // Qayta rasmiylashtirish bo'lmasa — null
    [property: JsonPropertyName("EmpowermentV2Id")]  string?          EmpowermentV2Id = null,
    [property: JsonPropertyName("OldEmpowermentDoc")] object?         OldEmpowermentDoc = null);

public sealed record EmpowermentDoc(
    [property: JsonPropertyName("No")]           string No,
    [property: JsonPropertyName("DateOfIssue")]  string DateOfIssue,
    [property: JsonPropertyName("DateOfExpire")] string DateOfExpire);

// ═══════════════════ 2. DOGOVOR NK ═══════════════════
// Didox: "7. Договор НК"

public sealed record ContractDocBlock(
    [property: JsonPropertyName("ContractName")]       string ContractName,
    [property: JsonPropertyName("ContractNo")]         string ContractNo,
    [property: JsonPropertyName("ContractDate")]       string ContractDate,
    [property: JsonPropertyName("ContractExpireDate")] string ContractExpireDate,
    [property: JsonPropertyName("ContractPlace")]      string ContractPlace);

public sealed record ContractParty(
    [property: JsonPropertyName("Tin")]        string  Tin,
    [property: JsonPropertyName("Name")]       string  Name,
    [property: JsonPropertyName("BranchCode")] string  BranchCode,
    [property: JsonPropertyName("BranchName")] string  BranchName,
    [property: JsonPropertyName("FizTin")]     string? FizTin,
    [property: JsonPropertyName("Fio")]        string? Fio,
    [property: JsonPropertyName("Address")]    string? Address,
    [property: JsonPropertyName("WorkPhone")]  string  WorkPhone,
    [property: JsonPropertyName("Mobile")]     string  Mobile,
    [property: JsonPropertyName("Oked")]       object? Oked,
    [property: JsonPropertyName("Account")]    string? Account,
    [property: JsonPropertyName("BankId")]     string? BankId);

public sealed record ContractPartBlock(
    [property: JsonPropertyName("ordno")] int    OrdNo,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")]  string Body);

public sealed record ContractProduct(
    [property: JsonPropertyName("OrdNo")]              int     OrdNo,
    [property: JsonPropertyName("Name")]               string  Name,
    [property: JsonPropertyName("CatalogCode")]        string? CatalogCode,
    [property: JsonPropertyName("CatalogName")]        string? CatalogName,
    [property: JsonPropertyName("Barcode")]            string  Barcode,
    [property: JsonPropertyName("MeasureId")]          string? MeasureId,
    [property: JsonPropertyName("PackageCode")]        string? PackageCode,
    [property: JsonPropertyName("PackageName")]        string? PackageName,
    [property: JsonPropertyName("Count")]              string  Count,
    [property: JsonPropertyName("Summa")]              string  Summa,
    [property: JsonPropertyName("DeliverySum")]        string  DeliverySum,
    [property: JsonPropertyName("VatRate")]            string  VatRate,
    [property: JsonPropertyName("VatSum")]             string  VatSum,
    [property: JsonPropertyName("DeliverySumWithVat")] string  DeliverySumWithVat,
    [property: JsonPropertyName("WithoutVat")]         bool    WithoutVat);

public sealed record ContractPayload(
    [property: JsonPropertyName("ContractDoc")] ContractDocBlock ContractDoc,
    [property: JsonPropertyName("Owner")]       ContractParty    Owner,
    [property: JsonPropertyName("Clients")]     IReadOnlyList<ContractParty>     Clients,
    [property: JsonPropertyName("Parts")]       IReadOnlyList<ContractPartBlock> Parts,
    [property: JsonPropertyName("Products")]    IReadOnlyList<ContractProduct>   Products,
    [property: JsonPropertyName("HasVat")]      bool             HasVat);

// ═══════════════════ 3. SCHYOT-FAKTURA (ЭСФ) ═══════════════════
// Didox: "1. Счёт фактура"
// DIQQAT: rasmiy namuna to'liq olinmagan — maydon nomlari hujjatdagi
// qat'iy qoidalar ro'yxatidan olindi. Test stendda tasdiqlash shart.

public sealed record FacturaDocBlock(
    [property: JsonPropertyName("FacturaNo")]   string FacturaNo,
    [property: JsonPropertyName("FacturaDate")] string FacturaDate);

public sealed record FacturaContractBlock(
    [property: JsonPropertyName("ContractNo")]   string ContractNo,
    [property: JsonPropertyName("ContractDate")] string ContractDate);

public sealed record FacturaParty(
    [property: JsonPropertyName("Name")]       string  Name,
    [property: JsonPropertyName("BranchCode")] string  BranchCode,
    [property: JsonPropertyName("BranchName")] string  BranchName,
    [property: JsonPropertyName("Account")]    string? Account,
    [property: JsonPropertyName("BankId")]     string? BankId,
    [property: JsonPropertyName("Address")]    string? Address,
    // Didox: "Director у поставщика и покупателя — обязательное поле"
    [property: JsonPropertyName("Director")]   string  Director,
    [property: JsonPropertyName("Accountant")] string? Accountant);

public sealed record FacturaProduct(
    [property: JsonPropertyName("OrdNo")]              int     OrdNo,
    [property: JsonPropertyName("Name")]               string  Name,
    [property: JsonPropertyName("CatalogCode")]        string? CatalogCode,
    [property: JsonPropertyName("CatalogName")]        string? CatalogName,
    [property: JsonPropertyName("Barcode")]            string  Barcode,
    [property: JsonPropertyName("PackageCode")]        string? PackageCode,
    [property: JsonPropertyName("PackageName")]        string? PackageName,
    [property: JsonPropertyName("Count")]              string  Count,
    [property: JsonPropertyName("Summa")]              string  Summa,
    [property: JsonPropertyName("DeliverySum")]        string  DeliverySum,
    [property: JsonPropertyName("VatRate")]            string  VatRate,
    [property: JsonPropertyName("VatSum")]             string  VatSum,
    [property: JsonPropertyName("DeliverySumWithVat")] string  DeliverySumWithVat,
    // WithoutVat FAQAT HasVat = true bo'lganda yuboriladi
    [property: JsonPropertyName("WithoutVat")]         bool?   WithoutVat);

public sealed record FacturaProductList(
    [property: JsonPropertyName("Tin")]                     string Tin,
    [property: JsonPropertyName("HasVat")]                  bool   HasVat,
    [property: JsonPropertyName("HasExcise")]               bool   HasExcise,
    [property: JsonPropertyName("Products")]                IReadOnlyList<FacturaProduct> Products,
    [property: JsonPropertyName("TotalDeliverySum")]        string TotalDeliverySum,
    [property: JsonPropertyName("TotalVatSum")]             string TotalVatSum,
    [property: JsonPropertyName("TotalDeliverySumWithVat")] string TotalDeliverySumWithVat);

public sealed record FacturaPayload(
    [property: JsonPropertyName("FacturaType")]  int                  FacturaType,
    [property: JsonPropertyName("FacturaDoc")]   FacturaDocBlock      FacturaDoc,
    [property: JsonPropertyName("ContractDoc")]  FacturaContractBlock ContractDoc,
    [property: JsonPropertyName("SellerTin")]    string               SellerTin,
    [property: JsonPropertyName("Seller")]       FacturaParty         Seller,
    [property: JsonPropertyName("BuyerTin")]     string               BuyerTin,
    [property: JsonPropertyName("Buyer")]        FacturaParty?        Buyer,
    [property: JsonPropertyName("ProductList")]  FacturaProductList   ProductList,
    // Ishlatilmasa BUTUNLAY null — bo'sh {} bo'lmasin
    [property: JsonPropertyName("FacturaRentDoc")]              object? FacturaRentDoc = null,
    [property: JsonPropertyName("OldFacturaDoc")]               object? OldFacturaDoc = null,
    [property: JsonPropertyName("ItemReleasedDoc")]             object? ItemReleasedDoc = null,
    [property: JsonPropertyName("FacturaInvestmentObjectDoc")]  object? FacturaInvestmentObjectDoc = null,
    [property: JsonPropertyName("FacturaEmpowermentDoc")]       object? FacturaEmpowermentDoc = null,
    [property: JsonPropertyName("ForeignCompany")]              object? ForeignCompany = null);

/// <summary>Schyot-faktura turlari — rasmiy hujjatdagi kodlar.</summary>
public static class FacturaType
{
    public const int Standard             = 0;
    public const int Additional           = 1;
    public const int ExpenseReimbursement = 2;
    public const int WithoutPayment       = 3;
    public const int Corrected            = 4;
    public const int CorrectedReimburse   = 5;
    public const int AdditionalReimburse  = 6;
    public const int CorrectedNoPayment   = 8;
    public const int AdditionalNoPayment  = 9;
}

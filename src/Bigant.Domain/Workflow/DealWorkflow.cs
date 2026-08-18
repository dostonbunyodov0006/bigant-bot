using Bigant.Domain.Entities;
using Bigant.Domain.Enums;

namespace Bigant.Domain.Workflow;

/// <summary>
/// Shartnoma cheklisti — "yopildimi?" savolining yagona manbai.
/// Bu yerda biznes qoidasi yashaydi, boshqa hech qayerda takrorlanmasin.
/// </summary>
public static class DealWorkflow
{
    /// <summary>Qadam ta'rifi: tartib, majburiylikmi, muddat qancha kun.</summary>
    public readonly record struct StepSpec(StepType Step, int Order, bool Required, int DueInDays);

    /// <summary>
    /// SOTUV (biz → klient):
    ///   dogovor → schyot na oplatu → pul → klient doverennosti → TTN → ESF → qabul
    /// </summary>
    private static readonly StepSpec[] SaleSteps =
    [
        new(StepType.Contract,          10, true,  1),
        new(StepType.InvoiceForPayment, 20, true,  1),
        new(StepType.Payment,           30, true,  5),
        new(StepType.Doverennost,       40, false, 7),   // haydovchi kelmasa bo'lmasligi mumkin
        new(StepType.Ttn,               50, true,  7),
        new(StepType.Esf,               60, true,  8),
        new(StepType.EsfAccepted,       70, true, 12),
    ];

    /// <summary>
    /// XARID (postavshik → biz):
    ///   dogovor keldi → biz doverennost chiqaramiz → TTN/ESF keldi → qabul → to'lov
    /// </summary>
    private static readonly StepSpec[] PurchaseSteps =
    [
        new(StepType.Contract,          10, true,  1),
        // Postavshik to'lov uchun hisob beradi — biz shunga qarab to'laymiz.
        new(StepType.InvoiceForPayment, 15, true,  2),
        new(StepType.Doverennost,       20, true,  3),
        new(StepType.Ttn,               30, false, 5),
        new(StepType.Esf,               40, true,  5),
        new(StepType.EsfAccepted,       50, true,  7),
        new(StepType.Payment,           60, true, 15),
    ];

    public static IReadOnlyList<StepSpec> Template(DealKind kind) =>
        kind == DealKind.Sale ? SaleSteps : PurchaseSteps;

    /// <summary>
    /// Yangi shartnomaga cheklist qatorlarini yaratadi.
    /// <paramref name="paymentDays"/> — otsrochka. Berilsa "To'lov" qadamining
    /// muddati shundan hisoblanadi, aks holda shablondagi standart qiymat.
    /// </summary>
    public static List<DealStep> BuildSteps(DealKind kind, DateOnly baseDate, int paymentDays = 0)
    {
        return Template(kind).Select(s =>
        {
            var days = s.Step == StepType.Payment && paymentDays > 0 ? paymentDays : s.DueInDays;
            return new DealStep
            {
                Step = s.Step,
                SortOrder = s.Order,
                IsRequired = s.Required,
                Status = StepStatus.Pending,
                DueDate = baseDate.AddDays(days)
            };
        }).ToList();
    }

    /// <summary>
    /// HUJJAT KIM TOMONIDAN CHIQARILADI.
    ///
    /// Umumiy qoida: sotuvda hujjatni biz chiqaramiz, xaridda postavshik.
    /// ISTISNO — DOVERENNOST: uni doim XARIDOR chiqaradi.
    ///   • Xaridda  → ekspeditorimiz tovarni olishi uchun BIZ chiqaramiz (Out)
    ///   • Sotuvda  → klient haydovchisi uchun KLIENT chiqaradi (In)
    /// </summary>
    /// <param name="contractByUs">
    /// Dogovorni kim tuzgani. null bo'lsa standart qoida ishlaydi.
    /// Bigant ham sotadi, ham sotib oladi — va dogovorni ikkala tomon ham
    /// yubora oladi, shuning uchun bu bitim darajasida tanlanadi.
    /// </param>
    /// <param name="ttnByUs">
    /// TTN ni kim rasmiylashtiradi. Bu YUKNI KIM TASHIYOTGANIGA bog'liq:
    /// o'z mashinamizda olib kelsak — biz, postavshik yetkazsa — u.
    /// null bo'lsa standart: sotuvda biz, xaridda ular.
    /// </param>
    public static DocDirection DirectionFor(DealKind kind, DocType doc,
        bool? contractByUs = null, bool? ttnByUs = null)
    {
        var weIssue = doc switch
        {
            // Ishonchnomani doim XARIDOR chiqaradi.
            DocType.Doverennost => kind == DealKind.Purchase,

            // Dogovorni kim tuzsa — o'sha yuboradi.
            DocType.Contract => contractByUs ?? (kind == DealKind.Sale),

            // TTN — kim tashishni tashkil qilsa, o'sha rasmiylashtiradi.
            DocType.Ttn => ttnByUs ?? (kind == DealKind.Sale),

            // Qolganini sotuvchi tomon chiqaradi.
            _ => kind == DealKind.Sale
        };
        return weIssue ? DocDirection.Out : DocDirection.In;
    }

    /// <summary>
    /// HUJJAT ZANJIRI — yangi hujjat qaysi hujjat ASOSIDA chiqarilgan.
    ///
    /// Haqiqiy hayotdagi tartib:
    ///   Dogovor  →  Schyot  →  Doverennost  →  TTN  →  Schyot-faktura
    ///
    /// Ya'ni faktura — tashilgan tovar (TTN) uchun, tovar esa ishonchnoma
    /// (doverennost) asosida berilgan, u ham dogovor bo'yicha. Shu bog'lanish
    /// «aynan shu dogovor uchun qaysi doverennost chiqarilgan» degan savolga
    /// javob beradi va Didox ESF dagi FacturaEmpowermentDoc ni to'ldiradi.
    ///
    /// Zanjirdagi eng yaqin mavjud hujjatni tanlaydi — oraliq hujjat bo'lmasa
    /// bir pog'ona yuqoriga chiqadi.
    /// </summary>
    public static readonly IReadOnlyList<DocType> Chain =
    [
        DocType.Contract,
        DocType.InvoiceForPayment,
        DocType.Doverennost,
        DocType.Ttn,
        DocType.Esf,
    ];

    /// <summary>
    /// <paramref name="existing"/> — bitimdagi mavjud hujjatlar (turi va id si).
    /// Bekor qilingan va rad etilganlar berilmasin.
    /// </summary>
    public static long? ResolveParent(
        DocType docType,
        IEnumerable<(DocType Type, long Id)> existing)
    {
        var idx = Chain.ToList().IndexOf(docType);
        if (idx <= 0) return null;   // Dogovor — zanjir boshi

        var byType = existing
            .GroupBy(x => x.Type)
            .ToDictionary(g => g.Key, g => g.Last().Id);

        // Eng yaqin oldingi hujjatdan boshlab orqaga qaraymiz.
        for (var i = idx - 1; i >= 0; i--)
            if (byType.TryGetValue(Chain[i], out var id))
                return id;

        return null;
    }

    /// <summary>Hujjat turi qaysi cheklist qadamini yopadi.</summary>
    public static StepType? StepForDocument(DocType docType) => docType switch
    {
        DocType.Contract          => StepType.Contract,
        DocType.InvoiceForPayment => StepType.InvoiceForPayment,
        DocType.Doverennost       => StepType.Doverennost,
        DocType.Ttn               => StepType.Ttn,
        DocType.Esf               => StepType.Esf,
        _ => null
    };

    /// <summary>
    /// Bitim yopilishi kerakmi. Barcha majburiy qadam Done bo'lsa — ha.
    /// Skipped ham "to'sqinlik qilmaydi" deb hisoblanadi.
    /// </summary>
    public static bool ShouldClose(Deal deal) =>
        deal.Status == DealStatus.Active &&
        deal.Steps.Where(s => s.IsRequired)
                  .All(s => s.Status is StepStatus.Done or StepStatus.Skipped);

    /// <summary>
    /// ENG XAVFLI HOLAT: pul keldi, lekin tovar berilmadi.
    /// Ombor yo'q — postavshik kechiksa klient pulini ushlab turgan bo'lasiz.
    /// </summary>
    public static bool IsPaidNotShipped(Deal deal)
    {
        if (deal.Kind != DealKind.Sale || deal.Status != DealStatus.Active) return false;
        var paid = deal.Steps.FirstOrDefault(s => s.Step == StepType.Payment);
        var ttn  = deal.Steps.FirstOrDefault(s => s.Step == StepType.Ttn);
        return paid?.Status == StepStatus.Done && ttn?.Status == StepStatus.Pending;
    }

    /// <summary>Tovar berildi, pul kelmadi — debitor qarz.</summary>
    public static bool IsShippedNotPaid(Deal deal)
    {
        if (deal.Kind != DealKind.Sale || deal.Status != DealStatus.Active) return false;
        var paid = deal.Steps.FirstOrDefault(s => s.Step == StepType.Payment);
        var ttn  = deal.Steps.FirstOrDefault(s => s.Step == StepType.Ttn);
        return ttn?.Status == StepStatus.Done && paid?.Status == StepStatus.Pending;
    }

    public static string StepLabel(StepType s) => s switch
    {
        StepType.Contract          => "Dogovor",
        StepType.InvoiceForPayment => "Schyot na oplatu",
        StepType.Payment           => "To'lov",
        StepType.Doverennost       => "Doverennost",
        StepType.Ttn               => "TTN",
        StepType.Esf               => "Schyot-faktura",
        StepType.EsfAccepted       => "Qarshi tomon qabul qildi",
        _ => s.ToString()
    };

    public static string DocLabel(DocType d) => d switch
    {
        DocType.Contract          => "Dogovor",
        DocType.InvoiceForPayment => "Schyot na oplatu",
        DocType.Doverennost       => "Doverennost",
        DocType.Ttn               => "TTN",
        DocType.Esf               => "Schyot-faktura",
        DocType.Act               => "Akt",
        DocType.Reconciliation    => "Akt sverki",
        _ => d.ToString()
    };

    /// <summary>Zanjirdagi bitta uzilish.</summary>
    public sealed record ChainBreak(DocType Doc, string Problem);

    /// <summary>
    /// ZANJIR TEKSHIRUVI — egasining asosiy talabi:
    ///   «aynan o'sha dogovorga doverennost chiqarildimi,
    ///    aynan o'sha doverennostga faktura chiqarildimi».
    ///
    /// Har bir hujjat o'zidan oldingi bo'g'inga ulanган bo'lishi kerak.
    /// Ulanmagan bo'lsa — uzilish, bugalter tekshirishi shart.
    /// </summary>
    public static IReadOnlyList<ChainBreak> CheckChain(
        IEnumerable<(DocType Type, long Id, long? ParentId)> docs)
    {
        var list = docs.ToList();
        var breaks = new List<ChainBreak>();
        if (list.Count == 0) return breaks;

        var ids = list.Select(d => d.Id).ToHashSet();
        var byType = list.GroupBy(d => d.Type).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var doc in list)
        {
            var idx = Chain.ToList().IndexOf(doc.Type);
            if (idx <= 0) continue;   // Dogovor — zanjir boshi, otasi shart emas

            if (doc.ParentId is null)
            {
                breaks.Add(new ChainBreak(doc.Type,
                    $"{DocLabel(doc.Type)} hech qanday hujjatga bog'lanmagan"));
                continue;
            }
            if (!ids.Contains(doc.ParentId.Value))
                breaks.Add(new ChainBreak(doc.Type,
                    $"{DocLabel(doc.Type)} asosi shu bitimda topilmadi"));
        }

        // Faktura/TTN bor, lekin doverennost umuman yo'q — eng muhim uzilish.
        var hasDov = byType.ContainsKey(DocType.Doverennost);
        if (!hasDov)
            foreach (var t in new[] { DocType.Ttn, DocType.Esf })
                if (byType.ContainsKey(t))
                    breaks.Add(new ChainBreak(t,
                        $"{DocLabel(t)} bor, lekin DOVERENNOST yo'q"));

        // Doverennost bor, lekin dogovor yo'q.
        if (hasDov && !byType.ContainsKey(DocType.Contract))
            breaks.Add(new ChainBreak(DocType.Doverennost, "Doverennost bor, lekin DOGOVOR yo'q"));

        return breaks;
    }

}

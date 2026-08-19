namespace Bigant.Domain.Enums;

/// <summary>
/// ROLLAR. Har biri faqat O'Z ISHINI ko'radi — ortiqcha ma'lumot chalg'itadi.
///
///   Realizatsiya — «Nima sotdik, kimga, qancha summaga, puli keldimi?»
///                  Sotuv bitimlari, faktura/TTN tekshiruvi, QQS, debitor qarz.
///
///   Perehod      — «Bu hujjat qaysi hujjatga tegishli, keyingi bosqichga
///                  qanday o'tadi?» Hujjat zanjiri, uzilishlar, kiruvchi hujjatlar.
///
///   Admin        — hammasini ko'radi, foydalanuvchi va sozlamalarni boshqaradi.
/// </summary>
public enum UserRole
{
    Realizatsiya = 1,
    Perehod = 2,
    Admin = 3
}

/// <summary>Bitim turi: klientga sotuv yoki postavshikdan xarid.</summary>
public enum DealKind
{
    Sale = 1,      // biz â klient
    Purchase = 2   // postavshik â biz
}

public enum DealStatus
{
    Draft = 1,      // sotuvchi hali yubormagan
    Active = 2,     // ketyapti, hujjatlar to'planyapti
    Closed = 3,     // hamma majburiy qadam bajarildi
    Cancelled = 4
}

/// <summary>Shartnoma cheklistidagi qadam turlari.</summary>
public enum StepType
{
    Contract = 1,           // dogovor imzolandi
    InvoiceForPayment = 2,  // schyot na oplatu chiqarildi
    Payment = 3,            // pul o'tdi
    Doverennost = 4,        // ishonchnoma
    Ttn = 5,                // tovar-transport nakladnoy
    Esf = 6,                // schyot-faktura chiqarildi
    EsfAccepted = 7         // qarshi tomon qabul qildi
}

public enum StepStatus
{
    Pending = 1,
    Done = 2,
    Skipped = 3,   // bu bitimda kerak emas (masalan doverennost bo'lmasa)
    Failed = 4     // rad etildi
}

/// <summary>Hujjat turi â Didox hujjat turlariga mos.</summary>
public enum DocType
{
    Contract = 1,
    InvoiceForPayment = 2,
    Doverennost = 3,
    Ttn = 4,
    Esf = 5,
    Act = 6,
    Reconciliation = 7   // akt sverki
}

public enum DocDirection
{
    In = 1,    // bizga keldi
    Out = 2    // biz yubordik
}

/// <summary>Hujjat holat mashinasi.</summary>
public enum DocStatus
{
    Draft = 1,
    ReadyToSign = 2,
    Signed = 3,
    Sent = 4,
    Delivered = 5,
    Accepted = 6,
    Rejected = 7,
    Cancelled = 8
}

public enum PaymentDirection
{
    In = 1,   // bizga tushdi
    Out = 2   // biz to'ladik
}

/// <summary>Eslatma turi â nima uchun chiqdi.</summary>
public enum NotificationKind
{
    IncomingDocument = 1,   // bizga yangi hujjat keldi (dogovor/faktura)
    DealOverdue = 2,        // shartnoma uzoq vaqt ochiq
    PaidNotShipped = 3,     // pul keldi, tovar berilmadi  â eng shoshilinch
    ShippedNotPaid = 4,     // tovar berildi, pul kelmadi
    StepOverdue = 5,        // cheklist qadami muddati o'tdi
    DocumentRejected = 6,   // qarshi tomon rad etdi
    DailyDigest = 7,        // ertalabki umumiy xulosa
    Payment = 8,            // to'lov tushdi (to'liq yoki qisman — qarz qoldi)

    // ── Realizatsiya buxgalteri ──
    CounterpartyIncomplete = 9,   // yangi kontragent — rekvizitlari to'liq emas
    Overpaid = 10,                // shartnoma summasidan ORTIQ to'landi
    DebtAging = 11,               // qarz 30 / 60 / 90 kundan oshdi
    DebtCleared = 12,             // qarz to'liq yopildi

    // ── Perehod buxgalteri ──
    UnlinkedDocument = 13,        // hujjat hech qaysi bitimga ilinmadi
    AmountMismatch = 14,          // hujjat summasi oldingi bo'g'indan farq qiladi
    ChainComplete = 15,           // zanjir to'liq — bitim yopishga tayyor
    DealStale = 16,               // bitim juda uzoq ochiq turibdi

    // ── Admin ──
    IntegrationDown = 17,         // Didox javob bermayapti
    JobFailed = 18,               // fon vazifasi xato berdi
    WeeklyDigest = 19,            // haftalik boshqaruv xulosasi

    // ══════════════════════════════════════════════════════════════
    //  QO'SHIMCHA NAZORAT (2026-08-19)
    //
    //  Hammasi Didox hujjatining O'ZIDA bor maydonlarga tayanadi:
    //  turi, raqami, sanasi, INN, nomi, summasi, QQS, yo'nalishi,
    //  ota-hujjat havolasi va doverennost muddati. Ya'ni Didox
    //  ulangan zahoti ishlaydi — qo'shimcha maydon so'ramaydi.
    // ══════════════════════════════════════════════════════════════

    // ── Realizatsiya buxgalteri: pul va soliq ──
    VatMissing = 20,              // fakturada QQS yo'q, shartnomada bor edi
    VatSuspicious = 21,           // QQS summaga nisbatan 12% dan keskin farq qiladi
    PaidWithoutInvoice = 22,      // pul o'tkazdik, faktura hali kelmadi — QQS hisobga olinmaydi
    BigDebtOnOne = 23,            // bitta kontragentdagi qarz belgilangan chegaradan oshdi
    MonthClosing = 24,            // oy tugayapti, yopilmagan bitimlar bor
    AdvancePayment = 25,          // hujjatsiz to'lov — avans

    // ── Perehod buxgalteri: hujjat va zanjir ──
    DoverennostExpiringSoon = 26, // doverennost muddati yaqinlashdi
    DoverennostExpired = 27,      // doverennost muddati o'tdi, tovar hali olinmadi
    TtnWithoutEsf = 28,           // tovar keldi, faktura kelmadi
    EsfWithoutTtn = 29,           // faktura keldi, tovar kelmadi
    DuplicateDocNumber = 30,      // bitta kontragentdan bir xil raqamli hujjat ikki marta
    ChainOutOfOrder = 31,         // hujjatlar noto'g'ri tartibda keldi
    InnMismatch = 32,             // hujjatdagi INN bitimdagi kontragentnikiga mos emas
    BackdatedDocument = 33,       // hujjat sanasi kelgan kunidan ancha oldin

    // ── Admin: tizim salomatligi ──
    NoDocumentsToday = 34,        // kun bo'yi Didox'dan bitta ham hujjat kelmadi

    // ══════════════════════════════════════════════════════════════
    //  OQIM BO'G'INLARI (2026-08-19)
    //
    //  Har bir hujjat topshiriladi va QARSHI TOMON javob berishini
    //  kutadi. Aynan shu kutish paytida ish to'xtab qoladi: doverennost
    //  muddati bilan chiqadi, tugamasidan qabul qilinishi kerak; qabul
    //  qilingach faktura kutiladi; keyin TTN. Har bo'g'inda alohida xabar.
    // ══════════════════════════════════════════════════════════════
    AwaitingAcceptance = 35,      // biz yubordik — qarshi tomon hali qabul qilmadi
    DocumentAccepted = 36,        // qarshi tomon qabul qildi — navbatdagi hujjat kutiladi
    NextDocExpected = 37,         // qabul qilingan, lekin keyingi hujjat kelmadi

    // ══════════════════════════════════════════════════════════════
    //  TO'LIQ QAMROV (2026-08-19)
    //
    //  Maqsad: bugalter Didox'da bo'lmagan paytda ham hech qanday
    //  holat e'tibordan chetda qolmasin. Hammasi Didox hujjatining
    //  o'zidagi maydonlarga tayanadi — qo'shimcha so'rov yo'q.
    // ══════════════════════════════════════════════════════════════

    // ── Hujjatning o'zidagi kamchilik (Perehod) ──
    DocumentWithoutNumber = 38,   // hujjat raqamsiz keldi
    DocumentWithoutAmount = 39,   // summa nol — hisobga olib bo'lmaydi
    FutureDatedDocument = 40,     // hujjat sanasi kelajakda
    DoverennostWithoutAgent = 41, // doverennostda ishonchli shaxs ko'rsatilmagan
    CounterpartyNameChanged = 42, // bir xil INN, boshqa nom
    SameDayDuplicate = 43,        // bir kunda bir xil summali bir xil hujjat
    ManyDocsFromOne = 44,         // bitta kontragentdan bir kunda juda ko'p hujjat
    EsfOverContract = 45,         // fakturalar yig'indisi shartnoma summasidan oshdi

    // ── Shartnoma yuritilishi (Perehod) ──
    ContractWithoutFollowUp = 46, // dogovor keldi, keyin hech narsa bo'lmadi
    DebtWithoutContract = 47,     // qarz bor, lekin dogovor hujjati yo'q
    ContractDueSoon = 48,         // shartnoma muddati tugayapti
    QuietCounterparty = 49,       // faol kontragentdan uzoq vaqt hujjat yo'q

    // ── Pul (Realizatsiya) ──
    PaymentBeforeContract = 50,   // to'lov sanasi dogovordan oldin
    DuplicatePaymentSameDay = 51, // bir kunda bir xil summa ikki marta
    LargePayment = 52,            // yirik to'lov — nazorat uchun
    ClientManyOverdue = 53,       // bitta klientda bir nechta kechikkan to'lov
    VatRateChanged = 54,          // QQS stavkasi hujjatlar orasida o'zgargan
    TurnoverDrop = 55,            // oylik aylanma keskin tushdi

    // ── Tizim (Admin) ──
    UnreadPileUp = 56,            // o'qilmagan xabarlar to'planib qoldi
    StaleUnlinkedDocs = 57        // bog'lanmagan hujjatlar uzoq turibdi
}

public enum NotificationSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3
}

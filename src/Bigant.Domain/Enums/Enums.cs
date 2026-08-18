namespace Bigant.Domain.Enums;

/// <summary>Foydalanuvchi roli.</summary>
public enum UserRole
{
    Accountant = 2,  // bugalter  â hujjat imzolaydi, to'lov kiritadi
    Manager = 3,  // rahbar   - hamma narsani ko'radi
    Admin = 4
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
    Payment = 8             // to'lov tushdi (to'liq yoki qisman — qarz qoldi)
}

public enum NotificationSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3
}

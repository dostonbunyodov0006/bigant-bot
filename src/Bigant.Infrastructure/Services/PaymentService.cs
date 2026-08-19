using System.Text.RegularExpressions;
using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bigant.Infrastructure.Services;

public sealed record NewPayment(
    long CounterpartyId,
    long? DealId,
    PaymentDirection Direction,
    decimal Amount,
    DateOnly PayDate,
    string? BankDocNo,
    string? Purpose);

public interface IPaymentService
{
    Task<Payment> RegisterAsync(NewPayment req, AppUser actor, CancellationToken ct = default);

    /// <summary>Bank vipiskasidan ko'p qatorni birdan import qiladi (avtomatik matching bilan).</summary>
    Task<(int imported, int matched)> ImportAsync(IEnumerable<NewPayment> rows, AppUser actor,
                                                  CancellationToken ct = default);
}

public sealed partial class PaymentService : IPaymentService
{
    private readonly AppDbContext _db;
    private readonly IDealService _deals;
    private readonly IAuditService _audit;
    private readonly INotificationService _notify;
    private readonly ILogger<PaymentService> _log;

    public PaymentService(AppDbContext db, IDealService deals, IAuditService audit,
                          INotificationService notify, ILogger<PaymentService> log)
    {
        _db = db; _deals = deals; _audit = audit; _notify = notify; _log = log;
    }

    public async Task<Payment> RegisterAsync(NewPayment req, AppUser actor, CancellationToken ct = default)
    {
        if (actor.Role is not (UserRole.Realizatsiya or UserRole.Perehod or UserRole.Admin))
            throw new BusinessException("FORBIDDEN", "To'lovni faqat bugalter kiritadi");
        if (req.Amount <= 0)
            throw new BusinessException("BAD_AMOUNT", "To'lov summasi musbat bo'lishi kerak");

        // Bank hujjati raqami bo'yicha dublikat oldini olish.
        if (!string.IsNullOrWhiteSpace(req.BankDocNo))
        {
            var dup = await _db.Payments.AnyAsync(p =>
                p.BankDocNo == req.BankDocNo && p.PayDate == req.PayDate && p.Amount == req.Amount, ct);
            if (dup)
                throw new BusinessException("DUPLICATE_PAYMENT",
                    $"Bu to'lov allaqachon kiritilgan (№{req.BankDocNo})");
        }

        var dealId = req.DealId ?? await MatchDealAsync(req, ct);

        var payment = new Payment
        {
            CounterpartyId = req.CounterpartyId,
            DealId = dealId,
            Direction = req.Direction,
            Amount = req.Amount,
            PayDate = req.PayDate,
            BankDocNo = req.BankDocNo,
            Purpose = req.Purpose,
            MatchedBy = req.DealId is null && dealId is not null ? "auto" : "manual",
            CreatedByUserId = actor.Id
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);

        if (dealId is { } id) await ApplyToDealAsync(id, actor, ct);

        _audit.Write(actor.Id, nameof(Payment), payment.Id, "register", null,
            new { req.Amount, req.PayDate, dealId });
        await _db.SaveChangesAsync(ct);

        return payment;
    }

    public async Task<(int imported, int matched)> ImportAsync(IEnumerable<NewPayment> rows, AppUser actor,
        CancellationToken ct = default)
    {
        int imported = 0, matched = 0;
        foreach (var r in rows)
        {
            try
            {
                var p = await RegisterAsync(r, actor, ct);
                imported++;
                if (p.DealId is not null) matched++;
            }
            catch (BusinessException ex)
            {
                _log.LogInformation("To'lov o'tkazib yuborildi ({Doc}): {Msg}", r.BankDocNo, ex.Message);
            }
        }
        return (imported, matched);
    }

    // ── Avtomatik matching ──────────────────────────────────────────
    /// <summary>
    /// To'lov maqsadidan shartnoma raqamini topadi (masalan "S-2026-00184 uchun to'lov").
    /// Topolmasa — kontragentning eng eski to'lanmagan bitimiga bog'lanadi.
    /// </summary>
    private async Task<long?> MatchDealAsync(NewPayment req, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(req.Purpose))
        {
            var m = DealNumberRegex().Match(req.Purpose);
            if (m.Success)
            {
                var number = m.Value.ToUpperInvariant();
                var byNumber = await _db.Deals
                    .Where(d => d.Number == number && d.CounterpartyId == req.CounterpartyId)
                    .Select(d => (long?)d.Id)
                    .FirstOrDefaultAsync(ct);
                if (byNumber is not null) return byNumber;
            }
        }

        var kind = req.Direction == PaymentDirection.In ? DealKind.Sale : DealKind.Purchase;

        // Summa aynan mos keladigan ochiq bitim — eng ishonchli variant.
        var exact = await _db.Deals
            .Where(d => d.CounterpartyId == req.CounterpartyId
                     && d.Kind == kind
                     && d.Status == DealStatus.Active
                     && d.AmountTotal - d.AmountPaid == req.Amount)
            .OrderBy(d => d.CreatedAt)
            .Select(d => (long?)d.Id)
            .FirstOrDefaultAsync(ct);
        if (exact is not null) return exact;

        // Aks holda eng eski to'lanmagan bitim (FIFO).
        return await _db.Deals
            .Where(d => d.CounterpartyId == req.CounterpartyId
                     && d.Kind == kind
                     && d.Status == DealStatus.Active
                     && d.AmountPaid < d.AmountTotal)
            .OrderBy(d => d.CreatedAt)
            .Select(d => (long?)d.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Bitimning to'langan summasini qayta hisoblab, to'lov qadamini yopadi.</summary>
    private async Task ApplyToDealAsync(long dealId, AppUser actor, CancellationToken ct)
    {
        var deal = await _db.Deals
            .Include(d => d.Steps)
            .Include(d => d.Payments)
            .Include(d => d.Counterparty)
            .FirstAsync(d => d.Id == dealId, ct);

        deal.AmountPaid = deal.Payments.Sum(p => p.Amount);

        var debt = deal.AmountTotal - deal.AmountPaid;
        var isFull = deal.AmountTotal > 0 && debt <= 0.01m;

        // QISMAN TO'LOV — bu normal holat: 5 mln, keyin 10 mln, qolgani qarz.
        // Cheklistdagi "To'lov" qadami ochiq turadi, izohida qancha qolgani yoziladi.
        var step = deal.Steps.FirstOrDefault(s => s.Step == StepType.Payment);
        if (step is not null && !isFull)
        {
            step.Status = StepStatus.Pending;
            step.Note = $"To'langan {deal.AmountPaid:N0} / {deal.AmountTotal:N0} — qarz {debt:N0} so'm";
        }
        await _db.SaveChangesAsync(ct);

        var buy = deal.Kind == DealKind.Purchase;
        var cpName = deal.Counterparty?.Name ?? "—";

        // Bir qarashda o'qiladigan ko'rinish: kim, qancha edi, qancha berildi,
        // qancha qoldi. Raqamlar ustma-ust turadi.
        // Shu safar qancha o'tkazilgani alohida ko'rsatiladi. Faqat «jami»
        // bo'lsa, bugalter oxirgi to'lov o'tdimi yoki yo'qmi bilolmaydi.
        var lastAmount = deal.Payments.OrderByDescending(x => x.Id)
                                      .Select(x => (decimal?)x.Amount)
                                      .FirstOrDefault();

        // «so'm» har qatorda turadi: hujjat xabarlarida ham shunday, bugalter
        // ikki xil ko'rinish orasida chalkashmasin.
        var body =
            // Shartnoma raqami sarlavhada bor — bu yerda takrorlanmaydi.
            $"Kontragent: {cpName}\n\n" +
            $"Bitim summasi:  {deal.AmountTotal:N0} so'm\n" +
            (lastAmount is { } la ? $"Bu safar:       {la:N0} so'm\n" : "") +
            (buy ? $"Jami o'tkazdik: {deal.AmountPaid:N0} so'm\n"
                 : $"Jami tushdi:    {deal.AmountPaid:N0} so'm\n") +
            "──────────────────\n" +
            (isFull
                ? "✅ QARZ YO'Q"
                : buy ? $"🔴 QOLDI TO'LASH: {debt:N0} so'm"
                      : $"🟢 QOLDI OLISH:  {debt:N0} so'm");

        // ── QARZ YOPILDI — alohida xabar, o'sha zahoti ──
        // «To'lov qabul qilindi» va «qarz butunlay yopildi» — ikki xil xabar.
        // Ikkinchisi yakunni bildiradi, bugalter bitimni ko'zdan chiqarishi mumkin.
        if (isFull)
        {
            await _notify.RaiseAsync(new Notification
            {
                Kind = NotificationKind.DebtCleared,
                TargetRole = UserRole.Realizatsiya,
                Severity = NotificationSeverity.Info,
                UserId = deal.ManagerId,
                DealId = deal.Id,
                Title = "✅ QARZ YOPILDI",
                Body = $"Kontragent: {cpName}\n" +
                       $"Shartnoma:  {deal.Number}\n\n" +
                       $"Jami: {deal.AmountTotal:N0} so'm\n" +
                       (buy ? "Biz to'liq to'ladik." : "Bizga to'liq tushdi.") +
                       "\nQarz qolmadi.",
                DedupKey = $"DebtCleared:deal:{deal.Id}"
            }, ct);
        }

        // ── ORTIQCHA TO'LOV — darhol, kiritilgan zahoti ──
        if (deal.AmountTotal > 0 && deal.AmountPaid > deal.AmountTotal)
        {
            var extra = deal.AmountPaid - deal.AmountTotal;
            await _notify.RaiseAsync(new Notification
            {
                Kind = NotificationKind.Overpaid,
                TargetRole = UserRole.Realizatsiya,
                Severity = NotificationSeverity.Warning,
                UserId = deal.ManagerId,
                DealId = deal.Id,
                Title = "\U0001F4B0 ORTIQCHA TO'LOV",
                Body = $"Kontragent: {cpName}\n" +
                       $"Shartnoma:  {deal.Number}\n\n" +
                       $"Shartnoma summasi: {deal.AmountTotal:N0}\n" +
                       $"To'langan:         {deal.AmountPaid:N0}\n" +
                       $"⚠️ Ortiqcha:        {extra:N0}\n\n" +
                       "Xato kiritilgan yoki avans bo'lishi mumkin — tekshiring.",
                DedupKey = $"Overpaid:deal:{deal.Id}:{deal.AmountPaid}"
            }, ct);
        }

        await _notify.RaiseAsync(new Notification
        {
            Kind = NotificationKind.Payment,
            TargetRole = UserRole.Realizatsiya,
            Severity = isFull ? NotificationSeverity.Info : NotificationSeverity.Warning,
            UserId = deal.ManagerId,
            DealId = deal.Id,
            Title = isFull
                ? $"To'liq to'landi — {deal.Number}"
                : buy ? $"To'lov o'tkazildi — {deal.Number}"
                      : $"To'lov tushdi — {deal.Number}",
            Body = body,
            DedupKey = $"Pay:{deal.Id}:{deal.AmountPaid}"
        }, ct);

        // XABARNI DARHOL YUBORAMIZ. Aks holda u bazada yotib, keyingi
        // fon vazifasini (30 daqiqagacha) kutardi — bugalter kech bilardi.
        await _notify.PushPendingAsync(ct);

        // To'liq to'langandagina cheklistdagi "To'lov" qadami yopiladi.
        if (isFull)
            await _deals.CompleteStepAsync(deal.Id, StepType.Payment, actor,
                note: $"To'liq to'landi: {deal.AmountPaid:N0}", ct: ct);
    }

    [GeneratedRegex(@"\b[SP]-\d{4}-\d{5}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DealNumberRegex();
}

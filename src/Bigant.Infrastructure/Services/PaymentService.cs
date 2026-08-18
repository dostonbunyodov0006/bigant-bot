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
        if (actor.Role is not (UserRole.Accountant or UserRole.Manager or UserRole.Admin))
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

        var who = deal.Kind == DealKind.Purchase ? "Biz to'ladik" : "Bizga tushdi";
        await _notify.RaiseAsync(new Notification
        {
            Kind = NotificationKind.Payment,
            Severity = isFull ? NotificationSeverity.Info : NotificationSeverity.Warning,
            UserId = deal.ManagerId,
            DealId = deal.Id,
            Title = isFull ? $"{deal.Number} — to'liq to'landi" : $"{deal.Number} — qisman to'lov",
            Body = isFull
                ? $"{who}: {deal.AmountPaid:N0} so'm. Qarz yo'q."
                : $"{who}: {deal.AmountPaid:N0} / {deal.AmountTotal:N0} so'm."
                  + $"\nQolgan qarz: {debt:N0} so'm.",
            DedupKey = $"Pay:{deal.Id}:{deal.AmountPaid}"
        }, ct);

        // To'liq to'langandagina cheklistdagi "To'lov" qadami yopiladi.
        if (isFull)
            await _deals.CompleteStepAsync(deal.Id, StepType.Payment, actor,
                note: $"To'liq to'landi: {deal.AmountPaid:N0}", ct: ct);
    }

    [GeneratedRegex(@"\b[SP]-\d{4}-\d{5}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DealNumberRegex();
}

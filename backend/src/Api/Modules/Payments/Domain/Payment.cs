using Api.Shared.Monetary;
using Api.Shared.Persistence;
using Api.Shared.Time;

namespace Api.Modules.Payments.Domain;

public enum PaymentMethod
{
    Cash,
    Card,
    Transfer,
    Other,
}

/// <summary>
/// A collection against an appointment. Append-only (ADR 0009): a payment is never edited or deleted. A mistake is
/// corrected with a reversal: another row with the negated amount that points at the original. Tenant data: RLS.
/// </summary>
public sealed class Payment : ITenantEntity
{
    public const int ReasonMaxLength = 200;

    private Payment()
    {
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid AppointmentId { get; private set; }

    /// <summary>Positive for a collection, negative for a reversal.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = Money.DefaultCurrency;
    public PaymentMethod Method { get; private set; }

    /// <summary>Set only on reversals: the payment this row cancels.</summary>
    public Guid? ReversesId { get; private set; }

    public string? Reason { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsReversal => ReversesId is not null;

    public static Payment Collect(Guid businessId, Guid appointmentId, Money amount, PaymentMethod method,
        Guid? userId, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount.Amount);
        return new Payment
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            AppointmentId = appointmentId,
            Amount = amount.Amount,
            Currency = amount.Currency,
            Method = method,
            CreatedByUserId = userId,
            CreatedAt = BusinessTimeZone.EnsureUtc(now),
        };
    }

    /// <summary>The inverse movement of <paramref name="original"/>, with the same method, appointment and currency.</summary>
    public static Payment Reverse(Payment original, string reason, Guid? userId, DateTimeOffset now)
    {
        if (original.IsReversal)
        {
            throw new InvalidOperationException("A reversal cannot be reversed.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var negated = new Money(original.Amount, original.Currency).Negate();
        return new Payment
        {
            Id = Guid.CreateVersion7(),
            BusinessId = original.BusinessId,
            AppointmentId = original.AppointmentId,
            Amount = negated.Amount,
            Currency = negated.Currency,
            Method = original.Method,
            ReversesId = original.Id,
            Reason = reason.Trim(),
            CreatedByUserId = userId,
            CreatedAt = BusinessTimeZone.EnsureUtc(now),
        };
    }
}

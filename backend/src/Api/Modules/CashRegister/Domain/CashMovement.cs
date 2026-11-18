using Api.Shared.Monetary;
using Api.Shared.Persistence;
using Api.Shared.Time;

namespace Api.Modules.CashRegister.Domain;

public enum CashKind
{
    Income,
    Expense,
}

/// <summary>
/// A manual cash movement (deposit or withdrawal). Append-only (ADR 0009): never edited or deleted; a mistake is
/// corrected with a reversal row of the opposite sign. Cash collected from appointments is not stored here: it is
/// read from the payments ledger. Tenant data: RLS by business_id.
/// </summary>
public sealed class CashMovement : ITenantEntity
{
    public const int ConceptMaxLength = 120;
    public const int ReasonMaxLength = 200;

    private CashMovement()
    {
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public CashKind Kind { get; private set; }

    /// <summary>Signed: positive increases the cash, negative decreases it.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = Money.DefaultCurrency;
    public string Concept { get; private set; } = string.Empty;
    public Guid? ReversesId { get; private set; }
    public string? Reason { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsReversal => ReversesId is not null;

    public static CashMovement Register(Guid businessId, CashKind kind, Money amount, string concept, Guid? userId,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount.Amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(concept);
        return new CashMovement
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            Kind = kind,
            Amount = kind == CashKind.Income ? amount.Amount : -amount.Amount,
            Currency = amount.Currency,
            Concept = concept.Trim(),
            CreatedByUserId = userId,
            CreatedAt = BusinessTimeZone.EnsureUtc(now),
        };
    }

    public static CashMovement Reverse(CashMovement original, string reason, Guid? userId, DateTimeOffset now)
    {
        if (original.IsReversal)
        {
            throw new InvalidOperationException("A reversal cannot be reversed.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new CashMovement
        {
            Id = Guid.CreateVersion7(),
            BusinessId = original.BusinessId,
            Kind = original.Kind,
            Amount = -original.Amount,
            Currency = original.Currency,
            Concept = original.Concept,
            ReversesId = original.Id,
            Reason = reason.Trim(),
            CreatedByUserId = userId,
            CreatedAt = BusinessTimeZone.EnsureUtc(now),
        };
    }
}

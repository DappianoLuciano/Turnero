using Api.Shared.Monetary;
using Api.Shared.Persistence;
using Api.Shared.Time;

namespace Api.Modules.CashRegister.Domain;

/// <summary>
/// The count of the cash drawer at the end of a local day. One per business and day, never modified (ADR 0009):
/// it freezes what was expected and what was actually counted. Tenant data: RLS by business_id.
/// </summary>
public sealed class CashClosing : ITenantEntity
{
    public const int NoteMaxLength = 300;

    private CashClosing()
    {
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }

    /// <summary>Local calendar day of the business that was closed.</summary>
    public DateOnly Date { get; private set; }

    public decimal Expected { get; private set; }
    public decimal Counted { get; private set; }

    /// <summary>Counted minus expected: negative means missing cash.</summary>
    public decimal Difference { get; private set; }

    public string Currency { get; private set; } = Money.DefaultCurrency;
    public string? Note { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static CashClosing Close(Guid businessId, DateOnly date, Money expected, Money counted, string? note,
        Guid? userId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            Date = date,
            Expected = expected.Amount,
            Counted = counted.Amount,
            Difference = (counted - expected).Amount,
            Currency = expected.Currency,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedByUserId = userId,
            CreatedAt = BusinessTimeZone.EnsureUtc(now),
        };
}

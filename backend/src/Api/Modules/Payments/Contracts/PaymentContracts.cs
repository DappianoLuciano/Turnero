namespace Api.Modules.Payments.Contracts;

/// <summary>Net amount collected with one payment method (collections minus reversals).</summary>
public sealed record PaymentMethodTotal(string Method, decimal Total);

/// <summary>Read access to the payment ledger of the current tenant. Use this instead of querying <c>payments</c>.</summary>
public interface IPaymentLedger
{
    /// <summary>Net totals per method of the movements registered in [<paramref name="from"/>, <paramref name="to"/>).</summary>
    Task<IReadOnlyList<PaymentMethodTotal>> GetTotalsByMethodAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

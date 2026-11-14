namespace Api.Shared.Money;

/// <summary>
/// Monetary amount. Stored as <c>numeric(12,2)</c> + <c>char(3)</c> currency (ISO 4217).
/// Never use float/double for money.
/// </summary>
public readonly record struct Money
{
    public const int Precision = 12;
    public const int Scale = 2;
    public const string DefaultCurrency = Currencies.Ars;

    public Money(decimal amount, string currency = DefaultCurrency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (currency.Length != 3)
        {
            throw new ArgumentException("Currency must be an ISO 4217 code.", nameof(currency));
        }

        if (decimal.Round(amount, Scale) != amount)
        {
            throw new ArgumentException($"Amount cannot have more than {Scale} decimals.", nameof(amount));
        }

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public decimal Amount { get; }
    public string Currency { get; }

    public static Money Zero(string currency = DefaultCurrency) => new(0m, currency);

    public Money Negate() => new(-Amount, Currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money Add(Money left, Money right) => left + right;
    public static Money Subtract(Money left, Money right) => left - right;

    public override string ToString() => $"{Amount:0.00} {Currency}";

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Currency mismatch: {left.Currency} vs {right.Currency}.");
        }
    }
}

public static class Currencies
{
    public const string Ars = "ARS";
}

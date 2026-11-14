using Api.Shared.Monetary;

namespace Api.Tests.Shared;

public sealed class MoneyTests
{
    [Fact]
    public void Defaults_to_ARS()
    {
        new Money(10m).Currency.ShouldBe("ARS");
    }

    [Fact]
    public void Rejects_more_than_two_decimals()
    {
        Should.Throw<ArgumentException>(() => new Money(1.005m));
    }

    [Fact]
    public void Adds_same_currency_and_rejects_mixed()
    {
        (new Money(1.50m) + new Money(2.25m)).Amount.ShouldBe(3.75m);
        Should.Throw<InvalidOperationException>(() => new Money(1m, "ARS") + new Money(1m, "USD"));
    }

    [Fact]
    public void Negate_produces_the_reversal_amount()
    {
        new Money(100m).Negate().Amount.ShouldBe(-100m);
    }
}

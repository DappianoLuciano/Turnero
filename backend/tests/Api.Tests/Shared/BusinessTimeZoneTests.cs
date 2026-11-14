using Api.Shared.Time;

namespace Api.Tests.Shared;

public sealed class BusinessTimeZoneTests
{
    [Fact]
    public void Default_zone_is_Buenos_Aires_and_resolves()
    {
        BusinessTimeZone.Default.ShouldBe("America/Argentina/Buenos_Aires");
        BusinessTimeZone.IsValid(BusinessTimeZone.Default).ShouldBeTrue();
    }

    [Fact]
    public void Converts_utc_to_local_and_back()
    {
        var utc = new DateTimeOffset(2026, 3, 10, 13, 0, 0, TimeSpan.Zero);

        var local = BusinessTimeZone.ToLocal(utc, BusinessTimeZone.Default);
        local.Hour.ShouldBe(10);
        local.Offset.ShouldBe(TimeSpan.FromHours(-3));

        var roundTrip = BusinessTimeZone.ToUtc(local.DateTime, BusinessTimeZone.Default);
        roundTrip.ShouldBe(utc);
        roundTrip.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Rejects_unknown_zone()
    {
        BusinessTimeZone.IsValid("Mars/Olympus").ShouldBeFalse();
    }
}

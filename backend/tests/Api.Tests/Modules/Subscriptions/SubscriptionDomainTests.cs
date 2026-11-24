using Api.Modules.Subscriptions.Contracts;
using Api.Modules.Subscriptions.Domain;

namespace Api.Tests.Modules.Subscriptions;

public sealed class SubscriptionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_subscription_is_a_two_day_trial()
    {
        var s = Subscription.StartTrial(Guid.NewGuid(), Now, 2);

        s.StateAt(Now).ShouldBe(SubscriptionState.Trial);
        s.StateAt(Now.AddDays(1.9)).ShouldBe(SubscriptionState.Trial);
        s.StateAt(Now.AddDays(2)).ShouldBe(SubscriptionState.Expired);
        s.SummaryAt(Now).DaysLeft.ShouldBe(2);
        s.SummaryAt(Now.AddDays(1.5)).DaysLeft.ShouldBe(1);
        s.SummaryAt(Now.AddDays(3)).DaysLeft.ShouldBe(0);
    }

    [Fact]
    public void Activating_after_the_trial_gives_a_paid_period_from_now()
    {
        var s = Subscription.StartTrial(Guid.NewGuid(), Now, 2);
        var later = Now.AddDays(5);

        s.Activate(later, 30);

        s.StateAt(later).ShouldBe(SubscriptionState.Active);
        s.PaidUntil.ShouldBe(later.AddDays(30));
        s.StateAt(later.AddDays(30)).ShouldBe(SubscriptionState.Expired);
    }

    [Fact]
    public void Activating_again_before_it_ends_extends_the_current_period()
    {
        var s = Subscription.StartTrial(Guid.NewGuid(), Now, 2);
        s.Activate(Now, 30);

        s.Activate(Now.AddDays(10), 30);

        s.PaidUntil.ShouldBe(Now.AddDays(60));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Subscription.MaxActivationDays + 1)]
    public void Activation_days_must_be_in_range(int days)
    {
        var s = Subscription.StartTrial(Guid.NewGuid(), Now, 2);

        Should.Throw<ArgumentOutOfRangeException>(() => s.Activate(Now, days));
    }
}

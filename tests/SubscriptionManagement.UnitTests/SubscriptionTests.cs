using SubscriptionManagement.Features.Subscriptions;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.UnitTests;

public class SubscriptionTests
{
    private static readonly DateOnly Start = new(2026, 9, 5);

    private static Subscription NewActive(DateOnly? trial = null) => Subscription.Create("Netflix", 99000m, Start, trial);

    [Fact]
    public void Create_starts_active_and_uses_trial_end_as_first_period_end()
    {
        var s = NewActive(trial: new DateOnly(2026, 9, 12));

        Assert.Equal(SubscriptionStatus.Active, s.Status);
        Assert.Equal(Start, s.CurrentPeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 12), s.CurrentPeriodEnd);
    }

    [Fact]
    public void Create_without_trial_bills_monthly()
    {
        Assert.Equal(new DateOnly(2026, 10, 5), NewActive().CurrentPeriodEnd);
    }

    [Fact]
    public void Create_clamps_month_end()
    {
        var s = Subscription.Create("X", 1m, new DateOnly(2026, 1, 31), null);
        Assert.Equal(new DateOnly(2026, 2, 28), s.CurrentPeriodEnd);
    }

    [Fact]
    public void Cancel_twice_is_a_400_with_the_original_message()
    {
        var s = NewActive();
        s.Cancel();

        var ex = Assert.Throws<ApiProblemException>(s.Cancel);

        Assert.Equal(400, ex.Status);
        Assert.Equal("subscription-already-canceled", ex.Slug);
        Assert.Equal("Subscription already canceled.", ex.Detail);
        Assert.Equal("Subscription already canceled.", ex.Extensions!["error"]);
    }

    [Fact]
    public void Pause_then_resume_round_trips()
    {
        var s = NewActive();
        s.Pause();
        Assert.Equal(SubscriptionStatus.Paused, s.Status);
        s.Resume();
        Assert.Equal(SubscriptionStatus.Active, s.Status);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Paused)]
    [InlineData(SubscriptionStatus.Canceled)]
    public void Pause_requires_active(SubscriptionStatus from)
    {
        var s = InState(from);
        var ex = Assert.Throws<ApiProblemException>(s.Pause);
        Assert.Equal(409, ex.Status);
        Assert.Equal("invalid-subscription-transition", ex.Slug);
        Assert.Equal(from.ToDb(), ex.Extensions!["from"]);
        Assert.Equal("PAUSED", ex.Extensions["to"]);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Canceled)]
    public void Resume_requires_paused(SubscriptionStatus from)
    {
        Assert.Equal(409, Assert.Throws<ApiProblemException>(InState(from).Resume).Status);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Paused)]
    [InlineData(SubscriptionStatus.Canceled)]
    public void Renew_requires_active(SubscriptionStatus from)
    {
        Assert.Equal(409, Assert.Throws<ApiProblemException>(() => InState(from).Renew()).Status);
    }

    [Fact]
    public void Renew_advances_one_month_from_the_current_period_end()
    {
        var s = NewActive(trial: new DateOnly(2026, 9, 12));

        var period = s.Renew();

        Assert.Equal(new BilledPeriod(new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12)), period);
        Assert.Equal(period.Start, s.CurrentPeriodStart);
        Assert.Equal(period.End, s.CurrentPeriodEnd);
    }

    private static Subscription InState(SubscriptionStatus status)
    {
        var s = NewActive();
        switch (status)
        {
            case SubscriptionStatus.Paused: s.Pause(); break;
            case SubscriptionStatus.Canceled: s.Cancel(); break;
        }

        return s;
    }
}

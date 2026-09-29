namespace SubscriptionManagement.Features.Subscriptions;

/// <summary>The interval an invoice covers.</summary>
public readonly record struct BilledPeriod(DateOnly Start, DateOnly End);

/// <summary>Subscription aggregate: identity, price and the lifecycle state machine.</summary>
public sealed class Subscription
{
    public Guid Id { get; }
    public string ServiceName { get; }
    public decimal Price { get; }
    public DateOnly StartDate { get; }
    public DateOnly? TrialEndsAt { get; }
    public SubscriptionStatus Status { get; private set; }

    // Internal billing-period tracking; not part of the public response.
    public DateOnly CurrentPeriodStart { get; private set; }
    public DateOnly CurrentPeriodEnd { get; private set; }

    private Subscription(
        Guid id, string serviceName, decimal price, DateOnly startDate, DateOnly? trialEndsAt,
        SubscriptionStatus status, DateOnly currentPeriodStart, DateOnly currentPeriodEnd)
    {
        Id = id;
        ServiceName = serviceName;
        Price = price;
        StartDate = startDate;
        TrialEndsAt = trialEndsAt;
        Status = status;
        CurrentPeriodStart = currentPeriodStart;
        CurrentPeriodEnd = currentPeriodEnd;
    }

    public static Subscription Create(string serviceName, decimal price, DateOnly startDate, DateOnly? trialEndsAt) => new(
        Guid.NewGuid(), serviceName, price, startDate, trialEndsAt, SubscriptionStatus.Active,
        currentPeriodStart: startDate,
        // The trial (when present) is the first billing period; otherwise assume monthly billing.
        currentPeriodEnd: trialEndsAt ?? startDate.AddMonths(1));

    public static Subscription Rehydrate(
        Guid id, string serviceName, decimal price, DateOnly startDate, DateOnly? trialEndsAt,
        SubscriptionStatus status, DateOnly currentPeriodStart, DateOnly currentPeriodEnd) =>
        new(id, serviceName, price, startDate, trialEndsAt, status, currentPeriodStart, currentPeriodEnd);

    public void Cancel()
    {
        if (Status == SubscriptionStatus.Canceled)
        {
            throw SubscriptionProblems.AlreadyCanceled();
        }

        Status = SubscriptionStatus.Canceled;
    }

    public void Pause()
    {
        if (Status != SubscriptionStatus.Active)
        {
            throw SubscriptionProblems.InvalidTransition(Status, SubscriptionStatus.Paused);
        }

        Status = SubscriptionStatus.Paused;
    }

    public void Resume()
    {
        if (Status != SubscriptionStatus.Paused)
        {
            throw SubscriptionProblems.InvalidTransition(Status, SubscriptionStatus.Active);
        }

        Status = SubscriptionStatus.Active;
    }

    /// <summary>Advances the billing period by one month and returns the period just entered. Only active subscriptions renew.</summary>
    public BilledPeriod Renew()
    {
        if (Status != SubscriptionStatus.Active)
        {
            throw SubscriptionProblems.InvalidTransition(Status, SubscriptionStatus.Active);
        }

        var period = new BilledPeriod(CurrentPeriodEnd, CurrentPeriodEnd.AddMonths(1));
        CurrentPeriodStart = period.Start;
        CurrentPeriodEnd = period.End;
        return period;
    }
}

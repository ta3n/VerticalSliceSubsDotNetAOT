using System.ComponentModel.DataAnnotations;

namespace SubscriptionManagement.Features.Subscriptions;

public class Subscription
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    [MaxLength(100)]
    public string ServiceName { get; private set; } = null!;
    public decimal Price { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? TrialEndsAt { get; private set; }
    public SubscriptionStatus Status { get; private set; }

    private Subscription() { }

    public Subscription(string serviceName, decimal price, DateOnly startDate, DateOnly? trialEndsAt)
    {
        ServiceName = serviceName;
        Price = price;
        StartDate = startDate;
        TrialEndsAt = trialEndsAt;
        Status = SubscriptionStatus.Active;
    }

    public void Cancel()
    {
        if (Status == SubscriptionStatus.Canceled)
        {
            throw new InvalidOperationException("Subscription already canceled.");
        }

        Status = SubscriptionStatus.Canceled;
    }
}
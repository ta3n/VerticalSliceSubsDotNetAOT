using System.Globalization;

namespace SubscriptionManagement.Billing.Infrastructure;

/// <summary>
/// <c>Billing:RenewalTimeoutSeconds</c>: how long a renewal waits for the payment reply before it is canceled.
/// <c>Billing:TaxRate</c>: fraction of the net price added as tax (0.10 = 10 %).
/// </summary>
public sealed record BillingOptions(
    TimeSpan RenewalTimeout,
    decimal TaxRate
)
{
    public const string SectionName = "Billing";

    public static BillingOptions From(
        IConfiguration configuration
    )
    {
        var section = configuration.GetSection(SectionName);

        var timeoutSeconds = section.GetValue<int>("RenewalTimeoutSeconds");
        if (timeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Billing:RenewalTimeoutSeconds must be positive.");
        }

        if (!decimal.TryParse(
                section["TaxRate"],
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var taxRate
            ))
        {
            throw new InvalidOperationException("Billing:TaxRate must be a non-negative decimal such as 0.10.");
        }

        return new BillingOptions(TimeSpan.FromSeconds(timeoutSeconds), taxRate);
    }
}
using Microsoft.Extensions.Configuration;
using SubscriptionManagement.Billing.Infrastructure;

namespace SubscriptionManagement.Billing.Tests;

public class BillingOptionsTests
{
    [Fact]
    public void Options_are_read_from_the_Billing_section()
    {
        var options = BillingOptions.From(
            Configuration(("Billing:RenewalTimeoutSeconds", "45"), ("Billing:TaxRate", "0.25"))
        );

        Assert.Equal(TimeSpan.FromSeconds(45), options.RenewalTimeout);
        Assert.Equal(0.25m, options.TaxRate);
    }

    [Theory]
    [InlineData("0", "0.10")]
    [InlineData("-5", "0.10")]
    [InlineData(null, "0.10")]
    [InlineData("30", "-0.10")]
    [InlineData("30", "ten percent")]
    [InlineData("30", null)]
    public void Invalid_options_fail_at_startup(
        string? timeoutSeconds,
        string? taxRate
    )
    {
        var configuration = Configuration(
            ("Billing:RenewalTimeoutSeconds", timeoutSeconds),
            ("Billing:TaxRate", taxRate)
        );

        Assert.Throws<InvalidOperationException>(() => BillingOptions.From(configuration));
    }

    private static IConfiguration Configuration(
        params (string Key, string? Value)[] values
    )
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();
    }
}
using SubscriptionManagement.Features.Invoices;
using SubscriptionManagement.Features.Subscriptions;

namespace SubscriptionManagement.UnitTests;

public class StatusMappingTests
{
    [Theory]
    [InlineData(SubscriptionStatus.Active, 1, "ACTIVE")]
    [InlineData(SubscriptionStatus.Canceled, 2, "CANCELED")]
    [InlineData(SubscriptionStatus.Paused, 3, "PAUSED")]
    public void Subscription_status_wire_and_db_values_are_stable(SubscriptionStatus status, int number, string db)
    {
        Assert.Equal(number, (int)status);
        Assert.Equal(db, status.ToDb());
        Assert.Equal(status, SubscriptionStatusNames.FromDb(db));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("")]
    [InlineData("DELETED")]
    public void Status_filter_parsing_is_exact(string value)
    {
        Assert.False(SubscriptionStatusNames.TryParse(value, out _));
    }

    [Theory]
    [InlineData(InvoiceStatus.Open, 1, "OPEN")]
    [InlineData(InvoiceStatus.Paid, 2, "PAID")]
    [InlineData(InvoiceStatus.Void, 3, "VOID")]
    public void Invoice_status_wire_and_db_values_are_stable(InvoiceStatus status, int number, string db)
    {
        Assert.Equal(number, (int)status);
        Assert.Equal(db, status.ToDb());
        Assert.Equal(status, InvoiceStatusNames.FromDb(db));
    }
}

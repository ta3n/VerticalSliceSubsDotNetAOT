using SubscriptionManagement.Features.Invoices;
using SubscriptionManagement.Features.Subscriptions;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.UnitTests;

public class InvoiceTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly BilledPeriod Period = new(new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12));

    [Fact]
    public void Issue_creates_an_open_invoice_rounded_to_the_column_scale()
    {
        var invoice = Invoice.Issue(Guid.NewGuid(), 10.123456m, "USD", Period, Now);

        Assert.Equal(InvoiceStatus.Open, invoice.Status);
        Assert.Equal(10.1235m, invoice.Amount);
        Assert.Null(invoice.PaidAt);
        Assert.Equal(DateTimeKind.Utc, invoice.IssuedAt.Kind);
    }

    [Fact]
    public void Pay_marks_paid_once()
    {
        var invoice = Invoice.Issue(Guid.NewGuid(), 5m, "USD", Period, Now);

        invoice.Pay(Now.AddMinutes(1));

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(Now.AddMinutes(1), invoice.PaidAt);

        var ex = Assert.Throws<ApiProblemException>(() => invoice.Pay(Now));
        Assert.Equal(409, ex.Status);
        Assert.Equal("invoice-not-payable", ex.Slug);
        Assert.Equal("PAID", ex.Extensions!["invoiceStatus"]);
    }

    [Fact]
    public void A_void_invoice_cannot_be_paid()
    {
        var invoice = Invoice.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), 1m, "USD", Period.Start, Period.End, InvoiceStatus.Void, Now, null);
        Assert.Equal(409, Assert.Throws<ApiProblemException>(() => invoice.Pay(Now)).Status);
    }
}

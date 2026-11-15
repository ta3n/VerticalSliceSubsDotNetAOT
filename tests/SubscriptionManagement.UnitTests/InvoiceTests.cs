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
        var invoice = Invoice.Rehydrate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1m,
            "USD",
            Period.Start,
            Period.End,
            InvoiceStatus.Void,
            Now,
            null
        );
        Assert.Equal(409, Assert.Throws<ApiProblemException>(() => invoice.Pay(Now)).Status);
    }

    [Fact]
    public void Settle_marks_an_open_invoice_paid_by_the_payment()
    {
        var invoice = Invoice.Issue(Guid.NewGuid(), 5m, "USD", Period, Now);
        var paymentId = Guid.NewGuid();

        invoice.Settle(paymentId, Now.AddMinutes(1));

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(Now.AddMinutes(1), invoice.PaidAt);
        Assert.Equal(paymentId, invoice.PaymentId);
    }

    [Fact]
    public void Settling_again_with_the_same_payment_changes_nothing()
    {
        var invoice = Invoice.Issue(Guid.NewGuid(), 5m, "USD", Period, Now);
        var paymentId = Guid.NewGuid();
        invoice.Settle(paymentId, Now.AddMinutes(1));

        invoice.Settle(paymentId, Now.AddMinutes(9)); // a redelivered command

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(Now.AddMinutes(1), invoice.PaidAt);
        Assert.Equal(paymentId, invoice.PaymentId);
    }

    [Fact]
    public void An_invoice_settled_by_another_payment_or_paid_by_hand_cannot_be_settled()
    {
        var settled = Invoice.Issue(Guid.NewGuid(), 5m, "USD", Period, Now);
        var first = Guid.NewGuid();
        settled.Settle(first, Now);
        var paidByHand = Invoice.Issue(Guid.NewGuid(), 5m, "USD", Period, Now);
        paidByHand.Pay(Now);

        foreach (var invoice in new[] { settled, paidByHand })
        {
            var ex = Assert.Throws<ApiProblemException>(() => invoice.Settle(Guid.NewGuid(), Now.AddMinutes(1)));
            Assert.Equal(409, ex.Status);
            Assert.Equal("invoice-not-payable", ex.Slug);
            Assert.Equal("PAID", ex.Extensions!["invoiceStatus"]);
        }

        Assert.Equal(first, settled.PaymentId); // the rejected attempt left the first payment in place
        Assert.Null(paidByHand.PaymentId);
    }

    [Fact]
    public void A_void_invoice_cannot_be_settled()
    {
        var invoice = Invoice.Issue(Guid.NewGuid(), 5m, "USD", Period, Now);
        invoice.Void();

        var ex = Assert.Throws<ApiProblemException>(() => invoice.Settle(Guid.NewGuid(), Now));

        Assert.Equal(409, ex.Status);
        Assert.Equal("invoice-not-payable", ex.Slug);
        Assert.Equal("VOID", ex.Extensions!["invoiceStatus"]);
        Assert.Null(invoice.PaymentId);
        Assert.Null(invoice.PaidAt);
    }

    [Fact]
    public void Void_cancels_an_open_invoice_and_is_repeatable()
    {
        var invoice = Invoice.Issue(Guid.NewGuid(), 5m, "USD", Period, Now);

        invoice.Void();
        Assert.Equal(InvoiceStatus.Void, invoice.Status);
        Assert.Null(invoice.PaidAt);

        invoice.Void(); // a redelivered command
        Assert.Equal(InvoiceStatus.Void, invoice.Status);
    }

    [Fact]
    public void A_paid_invoice_cannot_be_voided()
    {
        var paidByHand = Invoice.Issue(Guid.NewGuid(), 5m, "USD", Period, Now);
        paidByHand.Pay(Now);
        var settled = Invoice.Issue(Guid.NewGuid(), 5m, "USD", Period, Now);
        settled.Settle(Guid.NewGuid(), Now);

        foreach (var invoice in new[] { paidByHand, settled })
        {
            var ex = Assert.Throws<ApiProblemException>(invoice.Void);
            Assert.Equal(409, ex.Status);
            Assert.Equal("invoice-not-voidable", ex.Slug);
            Assert.Equal("PAID", ex.Extensions!["invoiceStatus"]);
            Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        }
    }

    [Fact]
    public void Rehydrate_restores_the_settling_payment()
    {
        var paymentId = Guid.NewGuid();
        var invoice = Invoice.Rehydrate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1m,
            "USD",
            Period.Start,
            Period.End,
            InvoiceStatus.Paid,
            Now,
            Now,
            paymentId
        );

        invoice.Settle(paymentId, Now.AddMinutes(1)); // same payment: accepted as a repeat

        Assert.Equal(paymentId, invoice.PaymentId);
        Assert.Equal(Now, invoice.PaidAt);
    }
}
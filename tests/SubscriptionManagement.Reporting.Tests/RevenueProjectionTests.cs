using SubscriptionManagement.Contracts;
using SubscriptionManagement.Reporting.Features.Revenue;

namespace SubscriptionManagement.Reporting.Tests;

public sealed class RevenueProjectionTests(
    ReportingFixture fixture
) : IClassFixture<ReportingFixture>
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 2, 1);

    private static InvoiceIssued Invoice(
        Guid id,
        decimal amount,
        string currency
    )
    {
        return new InvoiceIssued(id, Guid.NewGuid(), amount, currency, Start, End);
    }

    private static CurrencyRevenue Row(
        RevenueSummary summary,
        string currency
    )
    {
        return summary.Currencies.SingleOrDefault(c => c.Currency == currency)
            ?? new CurrencyRevenue(currency, 0, 0, 0, 0);
    }

    [Fact]
    public async Task Facts_from_the_topic_become_invoiced_collected_and_refunded_money()
    {
        // A currency of its own keeps this test apart from the others, which share the database.
        const string currency = "AAA";
        var paid = Guid.NewGuid();
        var unpaid = Guid.NewGuid();
        var failed = Guid.NewGuid();
        var paidPayment = Guid.NewGuid();
        var before = await fixture.WaitForAsync(_ => true);

        await fixture.PublishAsync(
            Invoice(paid, 100m, currency),
            Invoice(unpaid, 40m, currency),
            Invoice(failed, 10m, currency),
            new PaymentSucceeded(paid, paidPayment),
            new PaymentFailed(failed, Guid.NewGuid(), "declined"),
            new PaymentRefunded(paidPayment, paid, Guid.NewGuid(), 25m, currency, "canceled"),
            new SubscriptionCanceled(Guid.NewGuid(), End)
        );

        var summary = await fixture.WaitForAsync(
            s => Row(s, currency) is { InvoicesIssued: 3, Refunded: 25m }
                && s.SubscriptionsCanceled > before.SubscriptionsCanceled
        );

        var row = Row(summary, currency);
        Assert.Equal(150m, row.Invoiced);
        Assert.Equal(100m, row.Collected); // only the invoice whose payment succeeded
        Assert.Equal(25m, row.Refunded);
        Assert.Equal(before.PaymentsSucceeded + 1, summary.PaymentsSucceeded);
        Assert.Equal(before.PaymentsFailed + 1, summary.PaymentsFailed);
        Assert.NotNull(summary.PaymentSuccessRate);
    }

    [Fact]
    public async Task A_fact_delivered_twice_is_counted_once()
    {
        const string currency = "BBB";
        var invoice = Invoice(Guid.NewGuid(), 70m, currency);
        var payment = new PaymentSucceeded(invoice.InvoiceId, Guid.NewGuid());
        var marker = Invoice(Guid.NewGuid(), 1m, currency);

        // Kafka is at-least-once: the same messages again, then a marker that proves they were all consumed.
        await fixture.PublishAsync(invoice, payment, invoice, payment, marker);
        var summary = await fixture.WaitForAsync(s => Row(s, currency).InvoicesIssued == 2);

        var row = Row(summary, currency);
        Assert.Equal(71m, row.Invoiced);
        Assert.Equal(70m, row.Collected);
    }

    [Fact]
    public async Task A_payment_that_arrives_before_its_invoice_is_still_collected()
    {
        const string currency = "CCC";
        var invoice = Invoice(Guid.NewGuid(), 55m, currency);

        await fixture.PublishAsync(new PaymentSucceeded(invoice.InvoiceId, Guid.NewGuid()));
        await fixture.PublishAsync(invoice);

        var summary = await fixture.WaitForAsync(s => Row(s, currency).Collected == 55m);
        Assert.Equal(55m, Row(summary, currency).Invoiced);
    }
}
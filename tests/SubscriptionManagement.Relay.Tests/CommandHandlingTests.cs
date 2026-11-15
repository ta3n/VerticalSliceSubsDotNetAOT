using System.Net;
using SubscriptionManagement.Contracts;
using Wolverine.Tracking;

namespace SubscriptionManagement.Relay.Tests;

/// <summary>
/// The two commands through the running host: Wolverine finds the handlers, gives them the API client configured
/// from <c>Api:BaseUrl</c>, routes the replies to the broker and applies the retry policy of <see cref="ServiceSetup"/>.
/// </summary>
[Collection(RelayCollection.Name)]
public class CommandHandlingTests(
    RelayFixture fixture
)
{
    [Fact]
    public async Task SettleInvoice_calls_the_api_and_publishes_InvoiceSettled()
    {
        var command = new SettleInvoice(Guid.NewGuid(), Guid.NewGuid());
        fixture.Api.Respond(_ => HttpStatusCode.OK);

        var session = await fixture.App.InvokeMessageAndWaitAsync(command);

        Assert.Equal($"/invoices/{command.RenewalSagaId}/settle", Assert.Single(fixture.Api.Calls).Path);
        Assert.Equal(new InvoiceSettled(command.RenewalSagaId), Assert.Single(session.Sent.AllMessages()));
        Assert.Equal(
            "rabbitmq://exchange/SubscriptionManagement.Contracts.InvoiceSettled",
            session.Sent.SingleEnvelope<InvoiceSettled>().Destination!.ToString()
        );
    }

    [Fact]
    public async Task SettleInvoice_rejected_by_the_api_publishes_InvoiceSettlementRejected()
    {
        var command = new SettleInvoice(Guid.NewGuid(), Guid.NewGuid());
        fixture.Api.Respond(_ => HttpStatusCode.Conflict);

        var session = await fixture.App.InvokeMessageAndWaitAsync(command);

        Assert.Equal(
            new InvoiceSettlementRejected(command.RenewalSagaId, "invoice-not-payable"),
            Assert.Single(session.Sent.AllMessages())
        );
        Assert.Equal(
            "rabbitmq://exchange/SubscriptionManagement.Contracts.InvoiceSettlementRejected",
            session.Sent.SingleEnvelope<InvoiceSettlementRejected>().Destination!.ToString()
        );
    }

    [Fact]
    public async Task SettleInvoice_is_retried_after_a_500_and_answers_once_the_api_recovers()
    {
        var command = new SettleInvoice(Guid.NewGuid(), Guid.NewGuid());
        var attempts = 0;
        fixture.Api.Respond(
            _ => Interlocked.Increment(ref attempts) == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK
        );

        var session = await fixture.App.InvokeMessageAndWaitAsync(command);

        Assert.Equal(2, fixture.Api.Calls.Count);
        Assert.Equal(new InvoiceSettled(command.RenewalSagaId), Assert.Single(session.Sent.AllMessages()));
    }

    [Fact]
    public async Task SettleInvoice_is_retried_after_an_api_timeout_and_answers_once()
    {
        var command = new SettleInvoice(Guid.NewGuid(), Guid.NewGuid());
        var attempts = 0;
        fixture.Api.Respond(
            _ => Interlocked.Increment(ref attempts) == 1
                ? throw new TaskCanceledException("API request timed out.")
                : HttpStatusCode.OK
        );

        var session = await fixture.App.InvokeMessageAndWaitAsync(command);

        Assert.Equal(2, fixture.Api.Calls.Count);
        Assert.Equal(new InvoiceSettled(command.RenewalSagaId), Assert.Single(session.Sent.AllMessages()));
    }

    [Fact]
    public async Task CancelRenewal_voids_the_invoice_pauses_the_subscription_and_publishes_nothing()
    {
        var command = new CancelRenewal(Guid.NewGuid(), Guid.NewGuid(), "payment-declined");
        fixture.Api.Respond(_ => HttpStatusCode.OK);

        var session = await fixture.App.InvokeMessageAndWaitAsync(command);

        Assert.Equal(
            [$"/invoices/{command.InvoiceId}/void", $"/subscriptions/{command.SubscriptionId}/pause"],
            fixture.Api.Calls.Select(c => c.Path).ToArray()
        );
        Assert.Empty(session.Sent.AllMessages());
    }
}
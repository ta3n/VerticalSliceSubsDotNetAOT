using System.Net;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Relay.Features.Invoices.Settle;
using SubscriptionManagement.Relay.Infrastructure;

namespace SubscriptionManagement.Relay.Tests;

/// <summary>What the Relay tells the renewal saga for each answer of <c>POST /invoices/{id}/settle</c>.</summary>
public class SettleInvoiceHandlerTests
{
    private readonly StubApi _api = new();
    private readonly SettleInvoice _command = new(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public async Task Posts_the_payment_id_to_the_settle_endpoint_of_the_saga_invoice()
    {
        await Handle();

        var call = Assert.Single(_api.Calls);
        Assert.Equal("POST", call.Method);
        Assert.Equal($"/invoices/{_command.RenewalSagaId}/settle", call.Path);
        Assert.Equal($$"""{"paymentId":"{{_command.PaymentId}}"}""", call.Body);
    }

    [Fact]
    public async Task Ok_is_answered_with_InvoiceSettled()
    {
        _api.Respond(_ => HttpStatusCode.OK);

        Assert.Equal(new InvoiceSettled(_command.RenewalSagaId), await Handle());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "invoice-not-found")]
    [InlineData(HttpStatusCode.Conflict, "invoice-not-payable")]
    public async Task Not_found_and_conflict_are_answered_with_a_rejection(
        HttpStatusCode status,
        string reason
    )
    {
        _api.Respond(_ => status);

        Assert.Equal(new InvoiceSettlementRejected(_command.RenewalSagaId, reason), await Handle());
    }

    /// <summary>No reply at all: the exception is what makes Wolverine retry the command.</summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Any_other_status_throws_instead_of_answering(
        HttpStatusCode status
    )
    {
        _api.Respond(_ => status);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(Handle);

        Assert.Equal(status, failure.StatusCode);
    }

    private Task<object> Handle()
    {
        return SettleInvoiceHandler.Handle(
            _command,
            new ApiClient(new HttpClient(_api) { BaseAddress = new Uri("http://api.test") }),
            default
        );
    }
}
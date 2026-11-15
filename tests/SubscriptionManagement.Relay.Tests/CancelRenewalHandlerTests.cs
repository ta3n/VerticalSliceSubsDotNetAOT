using System.Net;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Relay.Features.Renewals.Cancel;
using SubscriptionManagement.Relay.Infrastructure;

namespace SubscriptionManagement.Relay.Tests;

/// <summary>The compensation of an uncollected renewal: void the invoice, then pause the subscription.</summary>
public class CancelRenewalHandlerTests
{
    private readonly StubApi _api = new();
    private readonly CancelRenewal _command = new(Guid.NewGuid(), Guid.NewGuid(), "payment-declined");

    private string VoidPath => $"/invoices/{_command.InvoiceId}/void";
    private string PausePath => $"/subscriptions/{_command.SubscriptionId}/pause";

    /// <summary>404 and 409 on the pause: the subscription is gone, or already paused or canceled.</summary>
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task A_voided_invoice_is_followed_by_a_pause_whose_answer_ends_the_command(
        HttpStatusCode pause
    )
    {
        _api.Respond(call => call.Path == PausePath ? pause : HttpStatusCode.OK);

        await Handle();

        Assert.Equal([new StubApi.Call("POST", VoidPath, null), new StubApi.Call("POST", PausePath, null)], _api.Calls);
    }

    /// <summary>409: the invoice was paid by other means. 404: there is no invoice to give up on.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task An_invoice_that_cannot_be_voided_leaves_the_subscription_alone(
        HttpStatusCode voided
    )
    {
        _api.Respond(_ => voided);

        await Handle();

        Assert.Equal([new StubApi.Call("POST", VoidPath, null)], _api.Calls);
    }

    [Fact]
    public async Task A_failing_void_throws_before_anything_is_paused()
    {
        _api.Respond(_ => HttpStatusCode.InternalServerError);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(Handle);

        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.Equal([new StubApi.Call("POST", VoidPath, null)], _api.Calls);
    }

    /// <summary>The redelivery voids again (a no-op in the API) and then pauses.</summary>
    [Fact]
    public async Task A_failing_pause_throws_so_the_command_is_delivered_again()
    {
        _api.Respond(call => call.Path == PausePath ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(Handle);

        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
    }

    private Task Handle()
    {
        return CancelRenewalHandler.Handle(
            _command,
            new ApiClient(new HttpClient(_api) { BaseAddress = new Uri("http://api.test") }),
            default
        );
    }
}
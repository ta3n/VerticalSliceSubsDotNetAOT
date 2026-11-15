using Grpc.Core;
using SubscriptionManagement.Contracts.Grpc;
using SubscriptionManagement.Features.Subscriptions;
using SubscriptionManagement.Infrastructure;
using SubscriptionManagement.Infrastructure.Billing;

namespace SubscriptionManagement.UnitTests;

/// <summary>How the gRPC gateway translates values and failures; the wire itself is covered by the integration tests.</summary>
public class BillingGatewayTests
{
    private static readonly BilledPeriod Period = new(new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12));

    [Fact]
    public async Task A_quote_request_carries_invariant_strings_and_the_reply_is_parsed_back_to_decimals()
    {
        var subscriptionId = Guid.NewGuid();
        var client = new StubClient
            { QuoteReply = new RenewalQuoteReply { Net = "12.50", Tax = "1.25", Total = "13.75", TaxRate = "0.10" } };

        var before = DateTime.UtcNow;
        var quote = await new GrpcBillingGateway(client).GetRenewalQuoteAsync(
            subscriptionId,
            12.50m,
            "USD",
            Period,
            CancellationToken.None
        );

        Assert.Equal(new RenewalQuote(12.50m, 1.25m, 13.75m), quote);
        var request = client.QuoteRequest!;
        Assert.Equal(subscriptionId.ToString(), request.SubscriptionId);
        Assert.Equal("12.50", request.Price);
        Assert.Equal("USD", request.Currency);
        Assert.Equal("2026-09-12", request.PeriodStart);
        Assert.Equal("2026-10-12", request.PeriodEnd);
        // Every call carries a 2 s deadline.
        Assert.InRange(client.Deadline!.Value, before.AddSeconds(2), DateTime.UtcNow.AddSeconds(2));
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task An_unreachable_or_slow_billing_is_a_503_quote_unavailable(
        StatusCode status
    )
    {
        var gateway = new GrpcBillingGateway(new StubClient { Failure = new RpcException(new Status(status, "boom")) });

        var ex = await Assert.ThrowsAsync<ApiProblemException>(
            () => gateway.GetRenewalQuoteAsync(Guid.NewGuid(), 1m, "USD", Period, CancellationToken.None)
        );

        Assert.Equal(503, ex.Status);
        Assert.Equal("quote-unavailable", ex.Slug);
        Assert.DoesNotContain("boom", ex.Detail);
    }

    [Fact]
    public async Task Other_quote_failures_are_not_disguised_as_unavailable()
    {
        var gateway = new GrpcBillingGateway(
            new StubClient { Failure = new RpcException(new Status(StatusCode.InvalidArgument, "bad price")) }
        );

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => gateway.GetRenewalQuoteAsync(Guid.NewGuid(), 1m, "USD", Period, CancellationToken.None)
        );

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task A_call_cancelled_because_the_caller_went_away_is_an_operation_cancelled()
    {
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();
        var gateway = new GrpcBillingGateway(
            new StubClient { Failure = new RpcException(new Status(StatusCode.Cancelled, "")) }
        );

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => gateway.GetRenewalQuoteAsync(Guid.NewGuid(), 1m, "USD", Period, gone.Token)
        );
        await Assert.ThrowsAsync<OperationCanceledException>(() => gateway.GetRenewalAsync(Guid.NewGuid(), gone.Token));
    }

    [Fact]
    public async Task A_renewal_reply_maps_ids_and_the_timestamp_and_turns_empty_strings_into_nulls()
    {
        var invoiceId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var client = new StubClient
        {
            RenewalReply = new RenewalReply
            {
                InvoiceId = invoiceId.ToString(), SubscriptionId = Guid.NewGuid().ToString(),
                PaymentId = paymentId.ToString(),
                State = "Declined", FailureReason = "card declined", UpdatedAt = "2026-09-30T12:00:01.5000000Z"
            }
        };

        var declined = await new GrpcBillingGateway(client).GetRenewalAsync(invoiceId, CancellationToken.None);

        Assert.Equal(invoiceId.ToString(), client.RenewalRequest!.InvoiceId);
        Assert.Equal(
            new RenewalStatus(
                invoiceId,
                paymentId,
                "Declined",
                "card declined",
                new DateTime(2026, 9, 30, 12, 0, 1, 500, DateTimeKind.Utc)
            ),
            declined
        );
        Assert.Equal(DateTimeKind.Utc, declined.UpdatedAt!.Value.Kind);

        // proto3 has no nulls: unset strings arrive empty.
        client.RenewalReply = new RenewalReply { InvoiceId = invoiceId.ToString(), State = "AwaitingPayment" };
        var awaiting = await new GrpcBillingGateway(client).GetRenewalAsync(invoiceId, CancellationToken.None);
        Assert.Equal(new RenewalStatus(invoiceId, null, "AwaitingPayment", null, null), awaiting);
    }

    [Fact]
    public async Task Renewal_status_failures_map_to_404_and_503()
    {
        var invoiceId = Guid.NewGuid();

        var notFound = await Assert.ThrowsAsync<ApiProblemException>(
            () =>
                new GrpcBillingGateway(
                        new StubClient { Failure = new RpcException(new Status(StatusCode.NotFound, "")) }
                    )
                    .GetRenewalAsync(invoiceId, CancellationToken.None)
        );
        Assert.Equal(404, notFound.Status);
        Assert.Equal("renewal-not-found", notFound.Slug);
        Assert.Equal(invoiceId.ToString(), notFound.Extensions!["invoiceId"]);

        var unavailable = await Assert.ThrowsAsync<ApiProblemException>(
            () =>
                new GrpcBillingGateway(
                        new StubClient { Failure = new RpcException(new Status(StatusCode.Unavailable, "")) }
                    )
                    .GetRenewalAsync(invoiceId, CancellationToken.None)
        );
        Assert.Equal(503, unavailable.Status);
        Assert.Equal("billing-unavailable", unavailable.Slug);
    }

    [Fact]
    public async Task Without_an_address_every_call_is_a_503_billing_not_configured()
    {
        var gateway = new UnconfiguredBillingGateway();

        var quote = await Assert.ThrowsAsync<ApiProblemException>(
            () => gateway.GetRenewalQuoteAsync(Guid.NewGuid(), 1m, "USD", Period, CancellationToken.None)
        );
        var renewal = await Assert.ThrowsAsync<ApiProblemException>(
            () => gateway.GetRenewalAsync(Guid.NewGuid(), CancellationToken.None)
        );

        Assert.All(
            [quote, renewal],
            ex =>
            {
                Assert.Equal(503, ex.Status);
                Assert.Equal("billing-not-configured", ex.Slug);
            }
        );
    }

    /// <summary>The generated client with its two calls replaced: records the request and deadline, then replies or fails.</summary>
    private sealed class StubClient : BillingService.BillingServiceClient
    {
        public RenewalQuoteReply? QuoteReply { get; set; }
        public RenewalReply? RenewalReply { get; set; }
        public RpcException? Failure { get; set; }
        public RenewalQuoteRequest? QuoteRequest { get; private set; }
        public GetRenewalRequest? RenewalRequest { get; private set; }
        public DateTime? Deadline { get; private set; }

        public override AsyncUnaryCall<RenewalQuoteReply> GetRenewalQuoteAsync(
            RenewalQuoteRequest request,
            CallOptions options
        )
        {
            QuoteRequest = request;
            Deadline = options.Deadline;
            return Call(QuoteReply);
        }

        public override AsyncUnaryCall<RenewalReply> GetRenewalAsync(
            GetRenewalRequest request,
            CallOptions options
        )
        {
            RenewalRequest = request;
            Deadline = options.Deadline;
            return Call(RenewalReply);
        }

        private AsyncUnaryCall<T> Call<T>(
            T? reply
        )
        {
            return new AsyncUnaryCall<T>(
                Failure is null ? Task.FromResult(reply!) : Task.FromException<T>(Failure),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => [],
                () => { }
            );
        }
    }
}
using System.Globalization;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.TestHost;
using SubscriptionManagement.Billing.Features.Renewals;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Contracts.Grpc;

namespace SubscriptionManagement.Billing.Tests;

/// <summary>
/// BillingService called over gRPC through the test host's pipeline. The calls are addressed by the method names of
/// billing.proto (<c>/billing.v1.BillingService/...</c>), which is what the API's generated client sends.
/// </summary>
[Collection(BillingCollection.Name)]
public sealed class BillingGrpcServiceTests : IDisposable
{
    private static readonly Method<RenewalQuoteRequest, RenewalQuoteReply> GetRenewalQuote =
        Unary("GetRenewalQuote", RenewalQuoteRequest.Parser, RenewalQuoteReply.Parser);

    private static readonly Method<GetRenewalRequest, RenewalReply> GetRenewal =
        Unary("GetRenewal", GetRenewalRequest.Parser, RenewalReply.Parser);

    private readonly BillingFixture _fixture;
    private readonly GrpcChannel _channel;

    public BillingGrpcServiceTests(
        BillingFixture fixture
    )
    {
        _fixture = fixture;
        _channel = GrpcChannel.ForAddress(
            fixture.Client.BaseAddress!,
            new GrpcChannelOptions { HttpHandler = fixture.App.GetTestServer().CreateHandler() }
        );
    }

    public void Dispose()
    {
        _channel.Dispose();
    }

    [Fact]
    public async Task Quote_adds_the_configured_tax_to_the_price()
    {
        // Billing:TaxRate is "0.10" in the service's appsettings.json.
        var reply = await CallAsync(GetRenewalQuote, ValidQuoteRequest("19.99"));

        Assert.Equal("19.9900", reply.Net);
        Assert.Equal("1.9990", reply.Tax);
        Assert.Equal("21.9890", reply.Total);
        Assert.Equal("0.10", reply.TaxRate);
    }

    [Fact]
    public async Task Quote_is_a_pure_function_of_the_request()
    {
        var request = ValidQuoteRequest("10.12345");

        var first = await CallAsync(GetRenewalQuote, request);
        var second = await CallAsync(GetRenewalQuote, request);

        Assert.Equal("11.1359", first.Total);
        Assert.Equal(first, second);
    }

    public static TheoryData<string, string, string, string, string> UnparsableQuoteRequests()
    {
        return new TheoryData<string, string, string, string, string>
        {
            { "not-a-uuid", "10", "USD", "2026-09-12", "2026-10-12" },
            { "", "10", "USD", "2026-09-12", "2026-10-12" },
            { ValidSubscriptionId, "ten", "USD", "2026-09-12", "2026-10-12" },
            { ValidSubscriptionId, "", "USD", "2026-09-12", "2026-10-12" },
            { ValidSubscriptionId, "-1", "USD", "2026-09-12", "2026-10-12" },
            { ValidSubscriptionId, "10,5", "USD", "2026-09-12", "2026-10-12" },
            { ValidSubscriptionId, "1e3", "USD", "2026-09-12", "2026-10-12" },
            { ValidSubscriptionId, "79228162514264337593543950335", "USD", "2026-09-12", "2026-10-12" },
            { ValidSubscriptionId, "10", "", "2026-09-12", "2026-10-12" },
            { ValidSubscriptionId, "10", "USD", "12/09/2026", "2026-10-12" },
            { ValidSubscriptionId, "10", "USD", "2026-09-12", "2026-13-01" },
            { ValidSubscriptionId, "10", "USD", "2026-09-12", "" },
            { ValidSubscriptionId, "10", "USD", "2026-10-12", "2026-09-12" }
        };
    }

    [Theory]
    [MemberData(nameof(UnparsableQuoteRequests))]
    public async Task Quote_rejects_values_it_cannot_use_as_INVALID_ARGUMENT(
        string subscriptionId,
        string price,
        string currency,
        string periodStart,
        string periodEnd
    )
    {
        var request = new RenewalQuoteRequest
        {
            SubscriptionId = subscriptionId, Price = price, Currency = currency, PeriodStart = periodStart,
            PeriodEnd = periodEnd
        };

        var failure = await Assert.ThrowsAsync<RpcException>(() => CallAsync(GetRenewalQuote, request));

        Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
    }

    [Fact]
    public async Task Renewal_is_NOT_FOUND_until_the_invoice_has_started_a_saga()
    {
        var issued = NewInvoice();
        var request = new GetRenewalRequest { InvoiceId = issued.InvoiceId.ToString() };

        var failure = await Assert.ThrowsAsync<RpcException>(() => CallAsync(GetRenewal, request));
        Assert.Equal(StatusCode.NotFound, failure.StatusCode);

        var before = DateTimeOffset.UtcNow;
        var started = await _fixture.Track().InvokeMessageAndWaitAsync(issued);
        var reply = await CallAsync(GetRenewal, request);

        Assert.Equal(issued.InvoiceId.ToString(), reply.InvoiceId);
        Assert.Equal(issued.SubscriptionId.ToString(), reply.SubscriptionId);
        Assert.Equal(started.Sent.SingleMessage<ChargePayment>().PaymentId.ToString(), reply.PaymentId);
        Assert.Equal("AwaitingPayment", reply.State);
        Assert.Equal("", reply.FailureReason);
        var updatedAt = DateTimeOffset.ParseExact(reply.UpdatedAt, "O", CultureInfo.InvariantCulture);
        Assert.Equal(TimeSpan.Zero, updatedAt.Offset);
        Assert.InRange(updatedAt, before.AddMilliseconds(-1), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Renewal_reports_the_terminal_state_and_why()
    {
        var issued = NewInvoice();
        var started = await _fixture.Track().InvokeMessageAndWaitAsync(issued);
        var paymentId = started.Sent.SingleMessage<ChargePayment>().PaymentId;
        await _fixture.Track()
            .InvokeMessageAndWaitAsync(new PaymentFailed(issued.InvoiceId, paymentId, "card declined"));

        var reply = await CallAsync(GetRenewal, new GetRenewalRequest { InvoiceId = issued.InvoiceId.ToString() });

        Assert.Equal(nameof(RenewalState.Declined), reply.State);
        Assert.Equal("card declined", reply.FailureReason);
        Assert.Equal(paymentId.ToString(), reply.PaymentId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("42")]
    public async Task Renewal_rejects_an_invoice_id_that_is_not_a_uuid(
        string invoiceId
    )
    {
        var failure = await Assert.ThrowsAsync<RpcException>(
            () => CallAsync(GetRenewal, new GetRenewalRequest { InvoiceId = invoiceId })
        );

        Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
    }

    private const string ValidSubscriptionId = "7b5f0c1e-64a8-4a0b-9d0c-2f6d6a3d1e11";

    private static RenewalQuoteRequest ValidQuoteRequest(
        string price
    )
    {
        return new RenewalQuoteRequest
        {
            SubscriptionId = ValidSubscriptionId, Price = price, Currency = "USD", PeriodStart = "2026-09-12",
            PeriodEnd = "2026-10-12"
        };
    }

    private static InvoiceIssued NewInvoice()
    {
        return new InvoiceIssued(
            Guid.NewGuid(),
            Guid.NewGuid(),
            11m,
            "USD",
            new DateOnly(2026, 9, 12),
            new DateOnly(2026, 10, 12)
        );
    }

    private async Task<TResponse> CallAsync<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        TRequest request
    )
        where TRequest : class
        where TResponse : class
    {
        using var call = _channel.CreateCallInvoker().AsyncUnaryCall(method, null, new CallOptions(), request);
        return await call.ResponseAsync;
    }

    // Billing generates the server half of billing.proto only, so the client half of a call is described here.
    private static Method<TRequest, TResponse> Unary<TRequest, TResponse>(
        string name,
        MessageParser<TRequest> requestParser,
        MessageParser<TResponse> responseParser
    )
        where TRequest : IMessage<TRequest>
        where TResponse : IMessage<TResponse>
    {
        return new Method<TRequest, TResponse>(
            MethodType.Unary,
            "billing.v1.BillingService",
            name,
            Marshallers.Create(message => message.ToByteArray(), requestParser.ParseFrom),
            Marshallers.Create(message => message.ToByteArray(), responseParser.ParseFrom)
        );
    }
}

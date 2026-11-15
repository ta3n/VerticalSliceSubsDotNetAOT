using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using SubscriptionManagement.Contracts.Grpc;

namespace SubscriptionManagement.IntegrationTests;

/// <summary>
/// The real gRPC client, wired by Program.cs from <c>Billing:GrpcAddress</c>, against a cleartext HTTP/2 server:
/// the generated client reaches it, and Billing's failures and slowness come back as problems instead of hanging.
/// </summary>
public sealed class GrpcBillingGatewayTests(
    ApiFixture fixture
) : ApiTestBase(fixture)
{
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> FactoryFor(
        string billingAddress
    )
    {
        return Fixture.CreateFactory(
            false,
            settings: new Dictionary<string, string>
            {
                // Read while Program.cs builds the app, so these go in as host settings.
                ["Billing:GrpcAddress"] = billingAddress,
                ["Outbox:Enabled"] = "true"
            }
        );
    }

    [Fact]
    public async Task A_renewal_is_priced_by_the_quote_fetched_over_grpc()
    {
        await using var billing = await BillingStub.StartAsync();
        billing.QuoteReply = new RenewalQuoteReply { Net = "12.5", Tax = "1.25", Total = "13.75", TaxRate = "0.10" };
        await using var factory = FactoryFor(billing.Address);
        using var client = factory.CreateClient();
        var id = Id(
            await CreateSubscriptionAsync(NewSubscription(price: 12.5m, start: "2026-09-05", trial: "2026-09-12"))
        );

        var response = await client.PostAsync($"/subscriptions/{id}/renewals", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(13.75m, (await ReadAsync(response)).GetProperty("invoice").GetProperty("amount").GetDecimal());
        var request = Assert.Single(billing.QuoteRequests);
        Assert.Equal(id, request.SubscriptionId);
        // The stored price is NUMERIC(57,28), so it travels with its full scale ("12.5000…"): compare the value.
        Assert.Equal(12.5m, decimal.Parse(request.Price, CultureInfo.InvariantCulture));
        Assert.Equal("USD", request.Currency);
        Assert.Equal("2026-09-12", request.PeriodStart);
        Assert.Equal("2026-10-12", request.PeriodEnd);
        Assert.Single(await Fixture.OutboxForAsync(id));
    }

    [Fact]
    public async Task A_billing_that_does_not_answer_within_the_deadline_is_a_503_and_nothing_is_written()
    {
        await using var billing = await BillingStub.StartAsync(); // no reply configured: the call hangs
        await using var factory = FactoryFor(billing.Address);
        using var client = factory.CreateClient();
        var id = Id(await CreateSubscriptionAsync());

        var clock = Stopwatch.StartNew();
        var response = await client.PostAsync($"/subscriptions/{id}/renewals", null);
        clock.Stop();

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "quote-unavailable");
        Assert.Single(billing.QuoteRequests); // an expired deadline is not retried
        Assert.InRange(
            clock.Elapsed,
            TimeSpan.FromSeconds(1.5),
            TimeSpan.FromSeconds(10)
        ); // the 2 s deadline, not a hang
        Assert.Empty(
            (await ReadAsync(await Client.GetAsync($"/subscriptions/{id}/invoices"))).GetProperty("items")
            .EnumerateArray()
        );
        Assert.Empty(await Fixture.OutboxForAsync(id));
    }

    [Fact]
    public async Task An_unreachable_billing_is_a_503_after_the_retries()
    {
        // Port 1 on loopback: nothing listens there, so every attempt fails immediately with Unavailable.
        await using var factory = FactoryFor("http://127.0.0.1:1");
        using var client = factory.CreateClient();
        var id = Id(await CreateSubscriptionAsync());

        await AssertProblemAsync(
            await client.PostAsync($"/subscriptions/{id}/renewals", null),
            HttpStatusCode.ServiceUnavailable,
            "quote-unavailable"
        );
        await AssertProblemAsync(
            await client.GetAsync($"/invoices/{Guid.NewGuid()}/renewal"),
            HttpStatusCode.ServiceUnavailable,
            "billing-unavailable"
        );

        Assert.Empty(await Fixture.OutboxForAsync(id));
    }

    [Fact]
    public async Task Renewal_status_is_read_over_grpc_and_not_found_becomes_a_404()
    {
        await using var billing = await BillingStub.StartAsync();
        await using var factory = FactoryFor(billing.Address);
        using var client = factory.CreateClient();
        var invoiceId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        await AssertProblemAsync(
            await client.GetAsync($"/invoices/{invoiceId}/renewal"),
            HttpStatusCode.NotFound,
            "renewal-not-found"
        );

        billing.RenewalReply = new RenewalReply
        {
            InvoiceId = invoiceId.ToString(), SubscriptionId = Guid.NewGuid().ToString(),
            PaymentId = paymentId.ToString(),
            State = "Completed", UpdatedAt = "2026-09-30T12:00:01.0000000Z"
        };
        var renewal = await ReadAsync(await client.GetAsync($"/invoices/{invoiceId}/renewal"));

        Assert.Equal([invoiceId.ToString(), invoiceId.ToString()], billing.RenewalRequests.Select(r => r.InvoiceId));
        Assert.Equal(invoiceId.ToString(), renewal.GetProperty("invoiceId").GetString());
        Assert.Equal(paymentId.ToString(), renewal.GetProperty("paymentId").GetString());
        Assert.Equal("Completed", renewal.GetProperty("state").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, renewal.GetProperty("failureReason").ValueKind);
        Assert.Equal("2026-09-30T12:00:01Z", renewal.GetProperty("updatedAt").GetString());
    }

    /// <summary>
    /// A minimal BillingService on Kestrel, HTTP/2 without TLS like the real one. It speaks the gRPC wire format by
    /// hand (a 5-byte prefix per message, the status in a trailer), because the server stubs are generated only in
    /// the Billing project. Without a configured reply GetRenewalQuote never answers and GetRenewal is NOT_FOUND.
    /// </summary>
    private sealed class BillingStub : IAsyncDisposable
    {
        private const int PrefixLength = 5; // compressed flag + big-endian message length
        private const string NotFound = "5";

        private readonly WebApplication _app;

        private BillingStub(
            WebApplication app
        )
        {
            _app = app;
        }

        public string Address => _app.Urls.Single();
        public RenewalQuoteReply? QuoteReply { get; set; }
        public RenewalReply? RenewalReply { get; set; }
        public List<RenewalQuoteRequest> QuoteRequests { get; } = [];
        public List<GetRenewalRequest> RenewalRequests { get; } = [];

        public static async Task<BillingStub> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(
                k => k.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2)
            );
            var stub = new BillingStub(builder.Build());

            stub._app.MapPost(
                "/billing.v1.BillingService/GetRenewalQuote",
                async context =>
                {
                    stub.QuoteRequests.Add(RenewalQuoteRequest.Parser.ParseFrom(await ReadMessageAsync(context)));
                    if (stub.QuoteReply is { } reply)
                    {
                        await WriteMessageAsync(context, reply);
                        return;
                    }

                    try
                    {
                        await Task.Delay(Timeout.Infinite, context.RequestAborted); // until the client gives up
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            );

            stub._app.MapPost(
                "/billing.v1.BillingService/GetRenewal",
                async context =>
                {
                    stub.RenewalRequests.Add(GetRenewalRequest.Parser.ParseFrom(await ReadMessageAsync(context)));
                    if (stub.RenewalReply is { } reply)
                    {
                        await WriteMessageAsync(context, reply);
                        return;
                    }

                    // A failed call has no message: the status travels in the response headers.
                    context.Response.ContentType = "application/grpc";
                    context.Response.Headers.GrpcStatus = NotFound;
                }
            );

            await stub._app.StartAsync();
            return stub;
        }

        public ValueTask DisposeAsync()
        {
            return _app.DisposeAsync();
        }

        private static async Task<byte[]> ReadMessageAsync(
            HttpContext context
        )
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body);
            return body.ToArray()[PrefixLength..];
        }

        private static async Task WriteMessageAsync(
            HttpContext context,
            IMessage reply
        )
        {
            var message = reply.ToByteArray();
            var frame = new byte[PrefixLength + message.Length];
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), message.Length);
            message.CopyTo(frame, PrefixLength);

            context.Response.ContentType = "application/grpc";
            await context.Response.Body.WriteAsync(frame);
            context.Response.AppendTrailer("grpc-status", "0");
        }
    }
}

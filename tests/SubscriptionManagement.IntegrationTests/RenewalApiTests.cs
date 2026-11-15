using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SubscriptionManagement.Features.Subscriptions;
using SubscriptionManagement.Infrastructure.Billing;

namespace SubscriptionManagement.IntegrationTests;

/// <summary>
/// POST /subscriptions/{id}/renewals (the saga-collected renewal), its status lookup, and the cancellation message:
/// what the API writes to the outbox, and that it does so in the business transaction or not at all.
/// </summary>
public sealed class RenewalApiTests : ApiTestBase
{
    public RenewalApiTests(
        ApiFixture fixture
    ) : base(fixture)
    {
        fixture.Billing.Reset();
    }

    private Task<HttpResponseMessage> StartRenewalAsync(
        string subscriptionId
    )
    {
        return PostAsync($"/subscriptions/{subscriptionId}/renewals");
    }

    private async Task<JsonElement[]> InvoicesAsync(
        string subscriptionId
    )
    {
        return (await ReadAsync(await Client.GetAsync($"/subscriptions/{subscriptionId}/invoices")))
            .GetProperty("items")
            .EnumerateArray()
            .ToArray();
    }

    [Fact]
    public async Task Starting_a_renewal_invoices_the_quoted_total_and_queues_InvoiceIssued_for_the_saga()
    {
        var id = Id(
            await CreateSubscriptionAsync(NewSubscription(price: 12.5m, start: "2026-09-05", trial: "2026-09-12"))
        );
        var traceId = ActivityTraceId.CreateRandom().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/subscriptions/{id}/renewals");
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        var before = DateTime.UtcNow;

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await ReadAsync(response);
        Assert.Equal(id, Id(result.GetProperty("subscription")));
        var invoice = result.GetProperty("invoice");
        var invoiceId = Id(invoice);
        Assert.Equal($"/invoices/{invoiceId}/renewal", response.Headers.Location?.OriginalString);
        Assert.Equal(id, invoice.GetProperty("subscriptionId").GetString());
        Assert.Equal(13.75m, invoice.GetProperty("amount").GetDecimal()); // the quote's total, not the price
        Assert.Equal("USD", invoice.GetProperty("currency").GetString());
        Assert.Equal("2026-09-12", invoice.GetProperty("periodStart").GetString());
        Assert.Equal("2026-10-12", invoice.GetProperty("periodEnd").GetString());
        Assert.Equal("OPEN", invoice.GetProperty("statusLabel").GetString());

        // Billing was asked once, for this subscription's price and the period being entered.
        var quoted = Assert.Single(Fixture.Billing.QuoteCalls);
        Assert.Equal(
            new FakeBillingGateway.QuoteCall(
                Guid.Parse(id),
                12.5m,
                "USD",
                new BilledPeriod(new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12))
            ),
            quoted
        );

        // Exactly one message, describing the stored invoice and carrying the request's trace.
        var message = Assert.Single(await Fixture.OutboxForAsync(id));
        Assert.Equal("InvoiceIssued", message.Type);
        Assert.Equal(invoiceId, message.Payload.GetProperty("invoiceId").GetString());
        Assert.Equal(id, message.Payload.GetProperty("subscriptionId").GetString());
        Assert.Equal(13.75m, message.Payload.GetProperty("amount").GetDecimal());
        Assert.Equal("USD", message.Payload.GetProperty("currency").GetString());
        Assert.Equal("2026-09-12", message.Payload.GetProperty("periodStart").GetString());
        Assert.Equal("2026-10-12", message.Payload.GetProperty("periodEnd").GetString());
        Assert.Equal(6, message.Payload.EnumerateObject().Count());
        Assert.StartsWith($"00-{traceId}-", message.Traceparent);
        Assert.Equal(55, message.Traceparent!.Length);
        Assert.InRange(message.OccurredAt, before.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));

        // The invoice is stored as returned, and the period advanced: the next renewal starts where this one ended.
        var stored = await ReadAsync(await Client.GetAsync($"/invoices/{invoiceId}"));
        Assert.Equal(13.75m, stored.GetProperty("amount").GetDecimal());
        var next = (await ReadAsync(await StartRenewalAsync(id))).GetProperty("invoice");
        Assert.Equal("2026-10-12", next.GetProperty("periodStart").GetString());
        Assert.Equal(2, (await Fixture.OutboxForAsync(id)).Count);
    }

    [Fact]
    public async Task A_renewal_that_fails_to_commit_leaves_no_invoice_no_message_and_the_period_untouched()
    {
        var id = Id(await CreateSubscriptionAsync(NewSubscription(start: "2026-09-05", trial: "2026-09-12")));

        // A deferred constraint trigger fires at COMMIT: after the period update, the invoice insert and the outbox
        // insert have all run. Only a shared transaction takes all three back.
        await Fixture.ExecuteAsync(
            "CREATE FUNCTION reject_commit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'commit rejected by test'; END $$"
        );
        await Fixture.ExecuteAsync(
            "CREATE CONSTRAINT TRIGGER reject_invoice_commit AFTER INSERT ON invoices DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_commit()"
        );
        try
        {
            await AssertProblemAsync(await StartRenewalAsync(id), HttpStatusCode.InternalServerError, "internal");
        }
        finally
        {
            await Fixture.ExecuteAsync("DROP TRIGGER reject_invoice_commit ON invoices");
            await Fixture.ExecuteAsync("DROP FUNCTION reject_commit()");
        }

        Assert.Single(Fixture.Billing.QuoteCalls); // it got as far as the transaction
        Assert.Empty(await InvoicesAsync(id));
        Assert.Empty(await Fixture.OutboxForAsync(id));

        var retried = (await ReadAsync(await StartRenewalAsync(id))).GetProperty("invoice");
        Assert.Equal(
            "2026-09-12",
            retried.GetProperty("periodStart").GetString()
        ); // not advanced by the failed attempt
        Assert.Single(await Fixture.OutboxForAsync(id));
    }

    [Fact]
    public async Task When_billing_cannot_quote_the_renewal_is_a_503_and_nothing_is_written()
    {
        var id = Id(await CreateSubscriptionAsync(NewSubscription(start: "2026-09-05", trial: "2026-09-12")));
        Fixture.Billing.Quote = _ => throw BillingProblems.QuoteUnavailable();

        await AssertProblemAsync(
            await StartRenewalAsync(id),
            HttpStatusCode.ServiceUnavailable,
            "quote-unavailable",
            $"/subscriptions/{id}/renewals"
        );

        Assert.Empty(await InvoicesAsync(id));
        Assert.Empty(await Fixture.OutboxForAsync(id));
        var renewed = (await ReadAsync(await PostAsync($"/subscriptions/{id}/renew"))).GetProperty("invoice");
        Assert.Equal("2026-09-12", renewed.GetProperty("periodStart").GetString()); // the period did not advance
    }

    [Fact]
    public async Task A_quote_too_large_for_the_invoice_column_is_a_400_and_queues_nothing()
    {
        var id = Id(await CreateSubscriptionAsync());
        Fixture.Billing.Quote = call => new RenewalQuote(call.Price, 0m, 12345678901234567890m);

        await AssertProblemAsync(await StartRenewalAsync(id), HttpStatusCode.BadRequest, "value-out-of-range");

        Assert.Empty(await InvoicesAsync(id));
        Assert.Empty(await Fixture.OutboxForAsync(id));
    }

    [Fact]
    public async Task Unknown_paused_and_canceled_subscriptions_are_rejected_without_asking_billing()
    {
        await AssertProblemAsync(
            await StartRenewalAsync(Guid.NewGuid().ToString()),
            HttpStatusCode.NotFound,
            "subscription-not-found"
        );

        var paused = Id(await CreateSubscriptionAsync());
        await PostAsync($"/subscriptions/{paused}/pause");
        var problem = await AssertProblemAsync(
            await StartRenewalAsync(paused),
            HttpStatusCode.Conflict,
            "invalid-subscription-transition"
        );
        Assert.Equal("PAUSED", problem.GetProperty("from").GetString());

        var canceled = Id(await CreateSubscriptionAsync());
        await Client.DeleteAsync($"/subscriptions/{canceled}");
        await AssertProblemAsync(
            await StartRenewalAsync(canceled),
            HttpStatusCode.Conflict,
            "invalid-subscription-transition"
        );

        Assert.Empty(Fixture.Billing.QuoteCalls);
        Assert.Empty(await Fixture.OutboxForAsync(paused));
        Assert.Empty(await Fixture.OutboxForAsync(canceled));
    }

    [Fact]
    public async Task Renewal_status_comes_from_billing_and_is_a_404_until_billing_knows_the_invoice()
    {
        var invoiceId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var missing = await AssertProblemAsync(
            await Client.GetAsync($"/invoices/{invoiceId}/renewal"),
            HttpStatusCode.NotFound,
            "renewal-not-found"
        );
        Assert.Equal(invoiceId.ToString(), missing.GetProperty("invoiceId").GetString());

        Fixture.Billing.Renewals[invoiceId] = new RenewalStatus(
            invoiceId,
            paymentId,
            "Declined",
            "card declined",
            new DateTime(2026, 9, 30, 12, 0, 1, DateTimeKind.Utc)
        );
        var response = await Client.GetAsync($"/invoices/{invoiceId}/renewal");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var declined = await ReadAsync(response);
        Assert.Equal(invoiceId.ToString(), declined.GetProperty("invoiceId").GetString());
        Assert.Equal(paymentId.ToString(), declined.GetProperty("paymentId").GetString());
        Assert.Equal("Declined", declined.GetProperty("state").GetString());
        Assert.Equal("card declined", declined.GetProperty("failureReason").GetString());
        Assert.Equal("2026-09-30T12:00:01Z", declined.GetProperty("updatedAt").GetString());
        Assert.Equal(5, declined.EnumerateObject().Count());

        Fixture.Billing.Renewals[invoiceId] = new RenewalStatus(invoiceId, paymentId, "AwaitingPayment", null, null);
        var awaiting = await ReadAsync(await Client.GetAsync($"/invoices/{invoiceId}/renewal"));
        Assert.Equal("AwaitingPayment", awaiting.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, awaiting.GetProperty("failureReason").ValueKind);
    }

    [Fact]
    public async Task Request_spans_carry_the_subscription_and_invoice_ids_that_the_trace_dashboard_searches_by()
    {
        var id = Id(await CreateSubscriptionAsync());

        var (started, renewalSpan) = await SendTracedAsync(HttpMethod.Post, $"/subscriptions/{id}/renewals");
        var invoiceId = Id((await ReadAsync(started)).GetProperty("invoice"));
        Assert.Equal(id, renewalSpan.GetTagItem("subscription.id"));
        Assert.Equal(invoiceId, renewalSpan.GetTagItem("invoice.id")); // known only after the invoice is issued

        Fixture.Billing.Renewals[Guid.Parse(invoiceId)] = new RenewalStatus(
            Guid.Parse(invoiceId),
            null,
            "AwaitingPayment",
            null,
            null
        );
        var (_, statusSpan) = await SendTracedAsync(HttpMethod.Get, $"/invoices/{invoiceId}/renewal");
        Assert.Equal(invoiceId, statusSpan.GetTagItem("invoice.id"));

        var (_, settleSpan) = await SendTracedAsync(
            HttpMethod.Post,
            $"/invoices/{invoiceId}/settle",
            new { paymentId = Guid.NewGuid() }
        );
        Assert.Equal(invoiceId, settleSpan.GetTagItem("invoice.id"));

        var (rejected, voidSpan) = await SendTracedAsync(HttpMethod.Post, $"/invoices/{invoiceId}/void");
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode); // tagged even when the call is refused
        Assert.Equal(invoiceId, voidSpan.GetTagItem("invoice.id"));
    }

    /// <summary>Sends a request as its own W3C trace and returns the response with the server span of that trace.</summary>
    private async Task<(HttpResponseMessage Response, Activity Span)> SendTracedAsync(
        HttpMethod method,
        string path,
        object? body = null
    )
    {
        var traceId = ActivityTraceId.CreateRandom();
        var stopped = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId) stopped.TrySetResult(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);

        using var request = new HttpRequestMessage(method, path)
            { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        var response = await Client.SendAsync(request);
        // The server span stops only after the response has been handed back.
        return (response, await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Without_a_billing_address_renewals_and_their_status_are_503_and_the_rest_of_the_api_works()
    {
        await using var factory = Fixture.CreateFactory(false); // as shipped: no address, outbox off
        using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/subscriptions", NewSubscription());
        var id = Id(await ReadAsync(created));

        await AssertProblemAsync(
            await client.PostAsync($"/subscriptions/{id}/renewals", null),
            HttpStatusCode.ServiceUnavailable,
            "billing-not-configured"
        );
        await AssertProblemAsync(
            await client.GetAsync($"/invoices/{Guid.NewGuid()}/renewal"),
            HttpStatusCode.ServiceUnavailable,
            "billing-not-configured"
        );

        Assert.Empty(await InvoicesAsync(id));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/subscriptions/{id}/renew", null)).StatusCode);
    }

    [Fact]
    public async Task With_billing_but_no_outbox_a_renewal_is_a_503_because_nothing_would_collect_it()
    {
        await using var factory = Fixture.CreateFactory(false, billing: Fixture.Billing)
            .WithWebHostBuilder(
                b =>
                    b.ConfigureAppConfiguration(
                        (_, config) =>
                            config.AddInMemoryCollection(
                                new Dictionary<string, string?> { ["Outbox:Enabled"] = "false" }
                            )
                    )
            );
        using var client = factory.CreateClient();
        var id = Id(await CreateSubscriptionAsync());

        await AssertProblemAsync(
            await client.PostAsync($"/subscriptions/{id}/renewals", null),
            HttpStatusCode.ServiceUnavailable,
            "billing-not-configured"
        );

        Assert.Empty(Fixture.Billing.QuoteCalls);
        Assert.Empty(await InvoicesAsync(id));
    }

    [Fact]
    public async Task Canceling_a_subscription_with_a_collected_payment_queues_SubscriptionCanceled_once()
    {
        var id = Id(await CreateSubscriptionAsync());
        var invoiceId = Id((await ReadAsync(await StartRenewalAsync(id))).GetProperty("invoice"));
        await Client.PostAsJsonAsync($"/invoices/{invoiceId}/settle", new { paymentId = Guid.NewGuid() });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/subscriptions/{id}")).StatusCode);
        // A repeated cancel is rejected, so it must not announce the cancellation again.
        await AssertProblemAsync(
            await Client.DeleteAsync($"/subscriptions/{id}"),
            HttpStatusCode.BadRequest,
            "subscription-already-canceled"
        );

        var canceled = Assert.Single(await Fixture.OutboxForAsync(id), m => m.Type == "SubscriptionCanceled");
        Assert.Equal(id, canceled.Payload.GetProperty("subscriptionId").GetString());
        var canceledOn = DateOnly.Parse(canceled.Payload.GetProperty("canceledOn").GetString()!);
        Assert.InRange(canceledOn, today, DateOnly.FromDateTime(DateTime.UtcNow)); // UTC date, even across midnight
        Assert.Equal(2, canceled.Payload.EnumerateObject().Count());
        Assert.Equal(55, canceled.Traceparent?.Length);
    }

    [Fact]
    public async Task Canceling_without_a_collected_payment_queues_nothing()
    {
        // No invoice at all; an invoice paid by hand; a saga invoice that was never settled.
        var plain = Id(await CreateSubscriptionAsync());
        var paidByHand = Id(await CreateSubscriptionAsync());
        var manualInvoice = Id(
            (await ReadAsync(await PostAsync($"/subscriptions/{paidByHand}/renew"))).GetProperty("invoice")
        );
        await PostAsync($"/invoices/{manualInvoice}/pay");
        var unsettled = Id(await CreateSubscriptionAsync());
        await StartRenewalAsync(unsettled);

        foreach (var id in new[] { plain, paidByHand, unsettled })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/subscriptions/{id}")).StatusCode);
            Assert.DoesNotContain(await Fixture.OutboxForAsync(id), m => m.Type == "SubscriptionCanceled");
        }
    }

    [Fact]
    public async Task With_the_outbox_off_canceling_queues_nothing_even_after_a_collected_payment()
    {
        await using var factory = Fixture.CreateFactory(false); // as shipped: outbox off
        using var client = factory.CreateClient();
        var id = Id(await ReadAsync(await client.PostAsJsonAsync("/subscriptions", NewSubscription())));
        var invoiceId = Id(
            (await ReadAsync(await client.PostAsync($"/subscriptions/{id}/renew", null))).GetProperty("invoice")
        );
        var settled = await client.PostAsJsonAsync($"/invoices/{invoiceId}/settle", new { paymentId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, settled.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/subscriptions/{id}")).StatusCode);

        Assert.Empty(await Fixture.OutboxForAsync(id));
    }
}
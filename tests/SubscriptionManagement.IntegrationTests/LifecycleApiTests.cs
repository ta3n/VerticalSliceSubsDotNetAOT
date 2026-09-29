using System.Net;

namespace SubscriptionManagement.IntegrationTests;

public sealed class LifecycleApiTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Pause_then_resume_updates_the_status_and_the_next_read_sees_it()
    {
        var id = Id(await CreateSubscriptionAsync());

        var paused = await ReadAsync(await PostAsync($"/subscriptions/{id}/pause"));
        Assert.Equal(3, paused.GetProperty("status").GetInt32());
        Assert.Equal(3, (await ReadAsync(await Client.GetAsync($"/subscriptions/{id}"))).GetProperty("status").GetInt32());

        var resumed = await ReadAsync(await PostAsync($"/subscriptions/{id}/resume"));
        Assert.Equal(1, resumed.GetProperty("status").GetInt32());
        Assert.Equal(1, (await ReadAsync(await Client.GetAsync($"/subscriptions/{id}"))).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Pausing_a_paused_subscription_is_a_409_naming_both_states()
    {
        var id = Id(await CreateSubscriptionAsync());
        await PostAsync($"/subscriptions/{id}/pause");

        var problem = await AssertProblemAsync(await PostAsync($"/subscriptions/{id}/pause"),
            HttpStatusCode.Conflict, "invalid-subscription-transition");
        Assert.Equal("PAUSED", problem.GetProperty("from").GetString());
        Assert.Equal("PAUSED", problem.GetProperty("to").GetString());
    }

    [Fact]
    public async Task Resuming_an_active_subscription_is_a_409()
    {
        var id = Id(await CreateSubscriptionAsync());
        await AssertProblemAsync(await PostAsync($"/subscriptions/{id}/resume"), HttpStatusCode.Conflict, "invalid-subscription-transition");
    }

    [Fact]
    public async Task A_canceled_subscription_cannot_be_paused_resumed_or_renewed()
    {
        var id = Id(await CreateSubscriptionAsync());
        await Client.DeleteAsync($"/subscriptions/{id}");

        foreach (var action in new[] { "pause", "resume", "renew" })
        {
            await AssertProblemAsync(await PostAsync($"/subscriptions/{id}/{action}"),
                HttpStatusCode.Conflict, "invalid-subscription-transition");
        }
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("renew")]
    public async Task Lifecycle_actions_on_an_unknown_subscription_are_404(string action)
    {
        await AssertProblemAsync(await PostAsync($"/subscriptions/{Guid.NewGuid()}/{action}"),
            HttpStatusCode.NotFound, "subscription-not-found");
    }

    [Fact]
    public async Task Renew_issues_an_open_usd_invoice_for_the_period_just_entered()
    {
        var subscription = await CreateSubscriptionAsync(NewSubscription(price: 12.5m, start: "2026-09-05", trial: "2026-09-12"));
        var id = Id(subscription);

        var response = await PostAsync($"/subscriptions/{id}/renew");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsync(response);

        Assert.Equal(id, Id(result.GetProperty("subscription")));
        var invoice = result.GetProperty("invoice");
        Assert.Equal(id, invoice.GetProperty("subscriptionId").GetString());
        Assert.Equal(12.5m, invoice.GetProperty("amount").GetDecimal());
        Assert.Equal("USD", invoice.GetProperty("currency").GetString());
        Assert.Equal("2026-09-12", invoice.GetProperty("periodStart").GetString());
        Assert.Equal("2026-10-12", invoice.GetProperty("periodEnd").GetString());
        Assert.Equal(1, invoice.GetProperty("status").GetInt32());
        Assert.Equal("OPEN", invoice.GetProperty("statusLabel").GetString());
        Assert.EndsWith("Z", invoice.GetProperty("issuedAt").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, invoice.GetProperty("paidAt").ValueKind);

        // Renewing again continues from the previous period end.
        var second = (await ReadAsync(await PostAsync($"/subscriptions/{id}/renew"))).GetProperty("invoice");
        Assert.Equal("2026-10-12", second.GetProperty("periodStart").GetString());
        Assert.Equal("2026-11-12", second.GetProperty("periodEnd").GetString());
    }

    [Fact]
    public async Task Invoices_are_listed_newest_first_fetchable_and_payable_exactly_once()
    {
        var id = Id(await CreateSubscriptionAsync());
        var first = (await ReadAsync(await PostAsync($"/subscriptions/{id}/renew"))).GetProperty("invoice");
        await Task.Delay(20);
        var second = (await ReadAsync(await PostAsync($"/subscriptions/{id}/renew"))).GetProperty("invoice");

        var list = await ReadAsync(await Client.GetAsync($"/subscriptions/{id}/invoices"));
        var items = list.GetProperty("items").EnumerateArray().Select(Id).ToArray();
        Assert.Equal([Id(second), Id(first)], items);

        var invoiceId = Id(first);
        var fetched = await ReadAsync(await Client.GetAsync($"/invoices/{invoiceId}"));
        Assert.Equal("OPEN", fetched.GetProperty("statusLabel").GetString());

        var paid = await ReadAsync(await PostAsync($"/invoices/{invoiceId}/pay"));
        Assert.Equal(2, paid.GetProperty("status").GetInt32());
        Assert.Equal("PAID", paid.GetProperty("statusLabel").GetString());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, paid.GetProperty("paidAt").ValueKind);

        var again = await AssertProblemAsync(await PostAsync($"/invoices/{invoiceId}/pay"),
            HttpStatusCode.Conflict, "invoice-not-payable");
        Assert.Equal("PAID", again.GetProperty("invoiceStatus").GetString());
    }

    [Fact]
    public async Task Concurrent_payments_of_one_invoice_succeed_exactly_once()
    {
        var id = Id(await CreateSubscriptionAsync());
        var invoiceId = Id((await ReadAsync(await PostAsync($"/subscriptions/{id}/renew"))).GetProperty("invoice"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostAsync($"/invoices/{invoiceId}/pay")));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Unknown_invoice_and_unknown_subscription_invoices_are_404()
    {
        await AssertProblemAsync(await Client.GetAsync($"/invoices/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "invoice-not-found");
        await AssertProblemAsync(await PostAsync($"/invoices/{Guid.NewGuid()}/pay"), HttpStatusCode.NotFound, "invoice-not-found");
        await AssertProblemAsync(await Client.GetAsync($"/subscriptions/{Guid.NewGuid()}/invoices"), HttpStatusCode.NotFound, "subscription-not-found");
    }

    [Fact]
    public async Task A_subscription_without_invoices_lists_an_empty_page()
    {
        var id = Id(await CreateSubscriptionAsync());
        var list = await ReadAsync(await Client.GetAsync($"/subscriptions/{id}/invoices"));
        Assert.Empty(list.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_renewal_price_beyond_the_invoice_column_is_a_400_and_leaves_the_period_untouched()
    {
        // 20 integer digits fit the subscription price column but not invoices.amount NUMERIC(20,4).
        var id = Id(await CreateSubscriptionAsync(NewSubscription(price: 12345678901234567890m)));

        await AssertProblemAsync(await PostAsync($"/subscriptions/{id}/renew"), HttpStatusCode.BadRequest, "value-out-of-range");

        var list = await ReadAsync(await Client.GetAsync($"/subscriptions/{id}/invoices"));
        Assert.Empty(list.GetProperty("items").EnumerateArray()); // transaction rolled back as a whole
    }
}

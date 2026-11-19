using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace SubscriptionManagement.IntegrationTests;

/// <summary>POST /invoices/{id}/settle and /void: the endpoints the Relay calls for the renewal saga, so repeats must be harmless.</summary>
public sealed class InvoiceSettlementApiTests(
  ApiFixture fixture
) : ApiTestBase(fixture)
{
  private async Task<string> NewOpenInvoiceAsync()
  {
    var id = Id(await CreateSubscriptionAsync());
    return Id((await ReadAsync(await PostAsync($"/subscriptions/{id}/renew"))).GetProperty("invoice"));
  }

  private Task<HttpResponseMessage> SettleAsync(
    string invoiceId,
    Guid paymentId
  )
  {
    return Client.PostAsJsonAsync($"/invoices/{invoiceId}/settle", new { paymentId });
  }

  private async Task<Guid?> StoredPaymentIdAsync(
    string invoiceId
  )
  {
    await using var connection = await Fixture.OpenDbAsync();
    await using var command = new NpgsqlCommand("SELECT payment_id FROM invoices WHERE id = $1", connection);
    command.Parameters.AddWithValue(Guid.Parse(invoiceId));
    return await command.ExecuteScalarAsync() as Guid?;
  }

  [Fact]
  public async Task A_payment_settled_after_cancellation_still_emits_the_refund_fact_exactly_once()
  {
    var subscriptionId = Id(await CreateSubscriptionAsync());
    var invoiceId = Id(
      (await ReadAsync(await PostAsync($"/subscriptions/{subscriptionId}/renew"))).GetProperty("invoice")
    );
    var paymentId = Guid.NewGuid();
    Assert.Equal(
      HttpStatusCode.NoContent,
      (await Client.DeleteAsync($"/subscriptions/{subscriptionId}")).StatusCode
    );
    Assert.Empty(await Fixture.OutboxForAsync(subscriptionId));

    Assert.Equal(HttpStatusCode.OK, (await SettleAsync(invoiceId, paymentId)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await SettleAsync(invoiceId, paymentId)).StatusCode);

    var fact = Assert.Single(await Fixture.OutboxForAsync(subscriptionId));
    Assert.Equal("SubscriptionCanceled", fact.Type);
    Assert.Equal(subscriptionId, fact.Payload.GetProperty("subscriptionId").GetString());
    Assert.Equal(paymentId, await StoredPaymentIdAsync(invoiceId));
  }

  [Fact]
  public async Task Cancellation_racing_settlement_always_queues_one_refund_fact()
  {
    var subscriptionId = Id(await CreateSubscriptionAsync());
    var invoiceId = Id(
      (await ReadAsync(await PostAsync($"/subscriptions/{subscriptionId}/renew"))).GetProperty("invoice")
    );
    var responses = await Task.WhenAll(
      SettleAsync(invoiceId, Guid.NewGuid()),
      Client.DeleteAsync($"/subscriptions/{subscriptionId}")
    );

    Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode);
    Assert.Equal("SubscriptionCanceled", Assert.Single(await Fixture.OutboxForAsync(subscriptionId)).Type);
  }

  [Fact]
  public async Task A_late_settlement_with_outbox_disabled_keeps_the_broker_behavior_off()
  {
    var subscriptionId = Id(await CreateSubscriptionAsync());
    var invoiceId = Id(
      (await ReadAsync(await PostAsync($"/subscriptions/{subscriptionId}/renew"))).GetProperty("invoice")
    );
    await Client.DeleteAsync($"/subscriptions/{subscriptionId}");
    await using var factory = Fixture.CreateFactory(
      false,
      settings: new Dictionary<string, string>
      {
        ["Outbox:Enabled"] = "false"
      }
    );
    using var client = factory.CreateClient();

    Assert.Equal(
      HttpStatusCode.OK,
      (await client.PostAsJsonAsync($"/invoices/{invoiceId}/settle", new { paymentId = Guid.NewGuid() }))
      .StatusCode
    );
    Assert.Empty(await Fixture.OutboxForAsync(subscriptionId));
  }

  [Fact]
  public async Task Settle_pays_the_invoice_records_the_payment_and_repeats_for_that_payment_change_nothing()
  {
    var invoiceId = await NewOpenInvoiceAsync();
    var paymentId = Guid.NewGuid();
    Assert.Null(await StoredPaymentIdAsync(invoiceId));

    var response = await SettleAsync(invoiceId, paymentId);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var settled = await ReadAsync(response);
    Assert.Equal(invoiceId, Id(settled));
    Assert.Equal(2, settled.GetProperty("status").GetInt32());
    Assert.Equal("PAID", settled.GetProperty("statusLabel").GetString());
    var paidAt = settled.GetProperty("paidAt").GetString();
    Assert.EndsWith("Z", paidAt);
    Assert.False(settled.TryGetProperty("paymentId", out _)); // the public invoice shape is unchanged
    Assert.Equal(paymentId, await StoredPaymentIdAsync(invoiceId));

    await Task.Delay(20); // a later clock, so a second write of paid_at would be visible
    var repeat = await SettleAsync(invoiceId, paymentId);
    Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
    Assert.Equal(paidAt, (await ReadAsync(repeat)).GetProperty("paidAt").GetString());
    Assert.Equal(
      paidAt,
      (await ReadAsync(await Client.GetAsync($"/invoices/{invoiceId}"))).GetProperty("paidAt").GetString()
    );
  }

  [Fact]
  public async Task Settling_with_another_payment_or_after_a_manual_payment_is_a_409_and_keeps_the_first_payment()
  {
    var settledInvoice = await NewOpenInvoiceAsync();
    var first = Guid.NewGuid();
    await SettleAsync(settledInvoice, first);
    var paidByHand = await NewOpenInvoiceAsync();
    await PostAsync($"/invoices/{paidByHand}/pay");

    foreach (var invoiceId in new[] { settledInvoice, paidByHand })
    {
      var problem = await AssertProblemAsync(
        await SettleAsync(invoiceId, Guid.NewGuid()),
        HttpStatusCode.Conflict,
        "invoice-not-payable",
        $"/invoices/{invoiceId}/settle"
      );
      Assert.Equal("PAID", problem.GetProperty("invoiceStatus").GetString());
    }

    Assert.Equal(first, await StoredPaymentIdAsync(settledInvoice));
    Assert.Null(await StoredPaymentIdAsync(paidByHand));
  }

  [Fact]
  public async Task A_settled_invoice_cannot_be_paid_by_hand()
  {
    var invoiceId = await NewOpenInvoiceAsync();
    await SettleAsync(invoiceId, Guid.NewGuid());

    await AssertProblemAsync(
      await PostAsync($"/invoices/{invoiceId}/pay"),
      HttpStatusCode.Conflict,
      "invoice-not-payable"
    );
  }

  [Fact]
  public async Task Concurrent_settlements_by_different_payments_succeed_exactly_once()
  {
    var invoiceId = await NewOpenInvoiceAsync();
    var payments = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();

    var responses = await Task.WhenAll(payments.Select(p => SettleAsync(invoiceId, p)));

    var winner = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
    Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    Assert.Equal(payments[Array.IndexOf(responses, winner)], await StoredPaymentIdAsync(invoiceId));
  }

  [Fact]
  public async Task Settle_requires_a_payment_id_and_a_known_invoice()
  {
    var invoiceId = await NewOpenInvoiceAsync();

    var missing = await AssertProblemAsync(
      await Client.PostAsJsonAsync($"/invoices/{invoiceId}/settle", new { }),
      HttpStatusCode.BadRequest,
      "validation"
    );
    Assert.Equal("must not be null", missing.GetProperty("errors").GetProperty("paymentId")[0].GetString());
    await AssertProblemAsync(
      await PostAsync($"/invoices/{invoiceId}/settle"),
      HttpStatusCode.BadRequest,
      "malformed-request"
    );
    await AssertProblemAsync(
      await Client.PostAsJsonAsync($"/invoices/{invoiceId}/settle", new { paymentId = "not-a-guid" }),
      HttpStatusCode.BadRequest,
      "malformed-request"
    );
    await AssertProblemAsync(
      await SettleAsync(Guid.NewGuid().ToString(), Guid.NewGuid()),
      HttpStatusCode.NotFound,
      "invoice-not-found"
    );

    // None of the rejected calls touched the invoice.
    Assert.Equal(
      "OPEN",
      (await ReadAsync(await Client.GetAsync($"/invoices/{invoiceId}"))).GetProperty("statusLabel").GetString()
    );
  }

  [Fact]
  public async Task Void_cancels_an_open_invoice_is_repeatable_and_blocks_later_payment()
  {
    var invoiceId = await NewOpenInvoiceAsync();

    var response = await PostAsync($"/invoices/{invoiceId}/void");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var voided = await ReadAsync(response);
    Assert.Equal(3, voided.GetProperty("status").GetInt32());
    Assert.Equal("VOID", voided.GetProperty("statusLabel").GetString());
    Assert.Equal(JsonValueKind.Null, voided.GetProperty("paidAt").ValueKind);

    var repeat = await PostAsync($"/invoices/{invoiceId}/void");
    Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
    Assert.Equal("VOID", (await ReadAsync(repeat)).GetProperty("statusLabel").GetString());

    foreach (var late in new[]
      { await PostAsync($"/invoices/{invoiceId}/pay"), await SettleAsync(invoiceId, Guid.NewGuid()) })
    {
      var problem = await AssertProblemAsync(late, HttpStatusCode.Conflict, "invoice-not-payable");
      Assert.Equal("VOID", problem.GetProperty("invoiceStatus").GetString());
    }

    Assert.Null(await StoredPaymentIdAsync(invoiceId));
  }

  [Fact]
  public async Task A_paid_invoice_cannot_be_voided_and_an_unknown_one_is_a_404()
  {
    var paidByHand = await NewOpenInvoiceAsync();
    await PostAsync($"/invoices/{paidByHand}/pay");
    var settled = await NewOpenInvoiceAsync();
    await SettleAsync(settled, Guid.NewGuid());

    foreach (var invoiceId in new[] { paidByHand, settled })
    {
      var problem = await AssertProblemAsync(
        await PostAsync($"/invoices/{invoiceId}/void"),
        HttpStatusCode.Conflict,
        "invoice-not-voidable",
        $"/invoices/{invoiceId}/void"
      );
      Assert.Equal("PAID", problem.GetProperty("invoiceStatus").GetString());
      Assert.Equal(invoiceId, problem.GetProperty("invoiceId").GetString());
      Assert.Equal(
        "PAID",
        (await ReadAsync(await Client.GetAsync($"/invoices/{invoiceId}"))).GetProperty("statusLabel")
        .GetString()
      );
    }

    await AssertProblemAsync(
      await PostAsync($"/invoices/{Guid.NewGuid()}/void"),
      HttpStatusCode.NotFound,
      "invoice-not-found"
    );
  }

  [Fact]
  public async Task A_void_racing_a_settlement_leaves_the_invoice_consistently_paid_or_void()
  {
    var invoiceId = await NewOpenInvoiceAsync();
    var paymentId = Guid.NewGuid();

    var settles = Enumerable.Range(0, 4).Select(_ => SettleAsync(invoiceId, paymentId));
    var voids = Enumerable.Range(0, 4).Select(_ => PostAsync($"/invoices/{invoiceId}/void"));
    var responses = await Task.WhenAll(settles.Concat(voids));
    var settled = responses.Take(4).Select(r => r.StatusCode).Distinct().ToArray();
    var voided = responses.Skip(4).Select(r => r.StatusCode).Distinct().ToArray();

    // Whichever took the row lock first wins for good: all of one kind succeed, all of the other conflict.
    var final = (await ReadAsync(await Client.GetAsync($"/invoices/{invoiceId}"))).GetProperty("statusLabel")
      .GetString();
    if (final == "PAID")
    {
      Assert.Equal([HttpStatusCode.OK], settled);
      Assert.Equal([HttpStatusCode.Conflict], voided);
      Assert.Equal(paymentId, await StoredPaymentIdAsync(invoiceId));
    }
    else
    {
      Assert.Equal("VOID", final);
      Assert.Equal([HttpStatusCode.Conflict], settled);
      Assert.Equal([HttpStatusCode.OK], voided);
      Assert.Null(await StoredPaymentIdAsync(invoiceId));
    }
  }
}
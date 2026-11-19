using System.Net;
using System.Text.Json;

namespace SubscriptionManagement.SystemTests;

/// <summary>
/// The renewal orchestration (Billing's saga) end to end: API -> gRPC quote -> outbox -> Relay -> RabbitMQ -> Billing ->
/// Payments (event-sourced) -> Billing -> Relay -> API. Each test is the HTTP contract plus what every service stored.
/// </summary>
public sealed class RenewalSagaSystemTests(
  SystemFixture system
) : SystemTestBase(system)
{
  [Fact]
  public async Task A_captured_charge_completes_the_renewal_and_pays_the_invoice()
  {
    var renewal = await StartRenewalAsync(SystemFixture.CapturedPrice);

    var status = await SettledAsync(renewal);

    Assert.Equal("Completed", status.GetProperty("state").GetString());
    Assert.Equal(renewal.InvoiceId, status.GetProperty("invoiceId").GetString());
    var paymentId = status.GetProperty("paymentId").GetString()!;
    var invoice = await InvoiceWithStatusAsync(renewal, "PAID");
    Assert.NotEqual(JsonValueKind.Null, invoice.GetProperty("paidAt").ValueKind);
    Assert.Equal(1, (await SubscriptionWithStatusAsync(renewal, 1)).GetProperty("status").GetInt32());

    // Payments stored the charge exactly as the invoice described it.
    var events = await PaymentEventsAsync(paymentId, "ChargeRequested", "ChargeCaptured");
    var charge = events.GetProperty("items")[0].GetProperty("data");
    Assert.Equal(renewal.InvoiceId, charge.GetProperty("invoiceId").GetString());
    Assert.Equal(renewal.SubscriptionId, charge.GetProperty("subscriptionId").GetString());
    Assert.Equal(renewal.Invoice.GetProperty("amount").GetDecimal(), charge.GetProperty("amount").GetDecimal());
    Assert.Equal(renewal.Invoice.GetProperty("currency").GetString(), charge.GetProperty("currency").GetString());
    var payment = await GetAsync(System.Payments, $"/payments/{paymentId}");
    Assert.Equal("Captured", payment.GetProperty("status").GetString());
  }

  [Fact]
  public async Task A_declined_charge_voids_the_invoice_and_pauses_the_subscription()
  {
    var renewal = await StartRenewalAsync(SystemFixture.DeclinedPrice);

    var status = await SettledAsync(renewal);

    Assert.Equal("Declined", status.GetProperty("state").GetString());
    await InvoiceWithStatusAsync(renewal, "VOID");
    await SubscriptionWithStatusAsync(renewal, 3);
    await PaymentEventsAsync(status.GetProperty("paymentId").GetString()!, "ChargeRequested", "ChargeDeclined");
  }

  [Fact]
  public async Task A_gateway_that_never_answers_times_out_voids_the_invoice_and_cancels_the_charge()
  {
    var renewal = await StartRenewalAsync(SystemFixture.SilentPrice);

    // The only way out is the saga's own timer (Billing:RenewalTimeoutSeconds), not a reply from Payments.
    var status = await SettledAsync(renewal);

    Assert.Equal("TimedOut", status.GetProperty("state").GetString());
    await InvoiceWithStatusAsync(renewal, "VOID");
    await SubscriptionWithStatusAsync(renewal, 3);
    await PaymentEventsAsync(status.GetProperty("paymentId").GetString()!, "ChargeRequested", "ChargeCanceled");
  }

  [Fact]
  public async Task Renewals_started_together_each_reach_their_own_outcome()
  {
    var prices = new[]
    {
      SystemFixture.CapturedPrice, SystemFixture.DeclinedPrice, SystemFixture.CapturedPrice,
      SystemFixture.DeclinedPrice, SystemFixture.CapturedPrice, SystemFixture.CapturedPrice
    };

    var outcomes = await Task.WhenAll(
      prices.Select(
        async price =>
        {
          var renewal = await StartRenewalAsync(price);
          var status = await SettledAsync(renewal);
          return (price, renewal, State: status.GetProperty("state").GetString(),
            PaymentId: status.GetProperty("paymentId").GetString());
        }
      )
    );

    // Nothing leaks between sagas: the outcome follows the price, and every renewal has its own payment.
    Assert.All(outcomes, o => Assert.Equal(o.price == SystemFixture.CapturedPrice ? "Completed" : "Declined", o.State));
    Assert.Equal(prices.Length, outcomes.Select(o => o.PaymentId).Distinct().Count());
    foreach (var outcome in outcomes)
      await InvoiceWithStatusAsync(outcome.renewal, outcome.price == SystemFixture.CapturedPrice ? "PAID" : "VOID");
  }

  [Fact]
  public async Task An_unknown_invoice_has_no_renewal()
  {
    var response = await System.Api.GetAsync($"/invoices/{Guid.NewGuid()}/renewal");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task A_renewal_started_while_the_relay_is_down_waits_in_the_outbox_and_completes_when_it_returns()
  {
    await System.StopRelayAsync();
    try
    {
      var renewal = await StartRenewalAsync(SystemFixture.CapturedPrice);

      // Committed with the invoice, in the API's own database; nothing else knows about the renewal yet.
      Assert.Equal(1, await OutboxRowsAsync(renewal.SubscriptionId));
      await Task.Delay(TimeSpan.FromSeconds(2));
      Assert.Equal(
        HttpStatusCode.NotFound,
        (await System.Api.GetAsync($"/invoices/{renewal.InvoiceId}/renewal")).StatusCode
      );
      Assert.Equal(1, await OutboxRowsAsync(renewal.SubscriptionId));

      await System.StartRelayAsync();

      Assert.Equal("Completed", (await SettledAsync(renewal)).GetProperty("state").GetString());
      await InvoiceWithStatusAsync(renewal, "PAID");
      // Relayed rows are deleted in the transaction that publishes them.
      await Eventually.UntilAsync(
        async () =>
        {
          var rows = await OutboxRowsAsync(renewal.SubscriptionId);
          return (rows == 0, rows);
        },
        Patience,
        "the relayed outbox row to be removed"
      );
    }
    finally
    {
      await System.StartRelayAsync();
    }
  }
}
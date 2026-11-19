using System.Net;
using System.Text.Json;

namespace SubscriptionManagement.SystemTests;

/// <summary>
/// The cancel -> refund -> credit note choreography: no service tells another what to do. The API announces
/// SubscriptionCanceled through its outbox, Payments reacts by refunding the captured charge, Billing reacts to
/// PaymentRefunded by issuing a credit note.
/// </summary>
public sealed class CancellationChoreographyTests(
  SystemFixture system
) : SystemTestBase(system)
{
  [Fact]
  public async Task Canceling_a_subscription_with_a_captured_payment_refunds_it_and_issues_one_credit_note()
  {
    var renewal = await StartRenewalAsync(SystemFixture.CapturedPrice);
    var paymentId = (await SettledAsync(renewal)).GetProperty("paymentId").GetString()!;
    await InvoiceWithStatusAsync(renewal, "PAID");

    var canceled = await System.Api.DeleteAsync($"/subscriptions/{renewal.SubscriptionId}");
    Assert.Equal(HttpStatusCode.NoContent, canceled.StatusCode);

    var note = (await CreditNotesAsync(renewal.SubscriptionId, 1)).GetProperty("items")[0];
    Assert.Equal(paymentId, note.GetProperty("paymentId").GetString());
    Assert.Equal(renewal.InvoiceId, note.GetProperty("invoiceId").GetString());
    Assert.Equal(renewal.SubscriptionId, note.GetProperty("subscriptionId").GetString());
    Assert.Equal(renewal.Invoice.GetProperty("amount").GetDecimal(), note.GetProperty("amount").GetDecimal());
    Assert.Equal(renewal.Invoice.GetProperty("currency").GetString(), note.GetProperty("currency").GetString());
    await PaymentEventsAsync(paymentId, "ChargeRequested", "ChargeCaptured", "ChargeRefunded");
    Assert.Equal(
      "Refunded",
      (await GetAsync(System.Payments, $"/payments/{paymentId}")).GetProperty("status").GetString()
    );
  }

  [Fact]
  public async Task Cancellation_reaches_Payments_even_when_the_relay_was_down_at_the_time()
  {
    var renewal = await StartRenewalAsync(SystemFixture.CapturedPrice);
    var paymentId = (await SettledAsync(renewal)).GetProperty("paymentId").GetString()!;
    await InvoiceWithStatusAsync(renewal, "PAID");

    await System.StopRelayAsync();
    try
    {
      Assert.Equal(
        HttpStatusCode.NoContent,
        (await System.Api.DeleteAsync($"/subscriptions/{renewal.SubscriptionId}")).StatusCode
      );
      // The fact is stored with the cancellation, and waits there: no refund can happen yet.
      Assert.Equal(1, await OutboxRowsAsync(renewal.SubscriptionId));
      await Task.Delay(TimeSpan.FromSeconds(2));
      Assert.Empty((await CreditNotesAsync(renewal.SubscriptionId, 0)).GetProperty("items").EnumerateArray());
    }
    finally
    {
      await System.StartRelayAsync();
    }

    await CreditNotesAsync(renewal.SubscriptionId, 1);
    await PaymentEventsAsync(paymentId, "ChargeRequested", "ChargeCaptured", "ChargeRefunded");
  }

  private Task<JsonElement> CreditNotesAsync(
    string subscriptionId,
    int expected
  )
  {
    return Eventually.UntilAsync(
      async () =>
      {
        var notes = await GetAsync(System.Billing, $"/credit-notes?subscriptionId={subscriptionId}");
        return (notes.GetProperty("items").GetArrayLength() == expected, notes);
      },
      Patience,
      $"{expected} credit note(s) for subscription {subscriptionId}"
    );
  }
}
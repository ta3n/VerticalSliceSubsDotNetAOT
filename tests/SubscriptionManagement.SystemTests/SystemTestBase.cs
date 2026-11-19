using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace SubscriptionManagement.SystemTests;

[Collection(SystemCollection.Name)]
public abstract class SystemTestBase(
  SystemFixture system
)
{
  /// <summary>States of a renewal that no message changes any more.</summary>
  public static readonly HashSet<string?> Terminal = ["Completed", "Declined", "TimedOut", "Compensated"];

  // Generous: the first use of a handler compiles it, and CI machines are slow. A test that passes finishes far sooner.
  protected static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

  protected SystemFixture System { get; } = system;

  protected async Task<JsonElement> CreateSubscriptionAsync(
    decimal price
  )
  {
    var response = await System.Api.PostAsJsonAsync("/subscriptions", SystemFixture.NewSubscription("System", price));
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    return await response.Content.ReadFromJsonAsync<JsonElement>();
  }

  /// <summary>Creates a subscription priced for the wanted gateway outcome and starts its renewal.</summary>
  protected async Task<Renewal> StartRenewalAsync(
    decimal price,
    string? traceparent = null
  )
  {
    var subscriptionId = (await CreateSubscriptionAsync(price)).GetProperty("id").GetString()!;
    using var request = new HttpRequestMessage(HttpMethod.Post, $"/subscriptions/{subscriptionId}/renewals");
    if (traceparent is not null) request.Headers.Add("traceparent", traceparent);

    var response = await System.Api.SendAsync(request);
    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    var invoice = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("invoice");
    Assert.Equal(subscriptionId, invoice.GetProperty("subscriptionId").GetString());
    return new Renewal(subscriptionId, invoice.GetProperty("id").GetString()!, invoice);
  }

  /// <summary>Waits until Billing's saga for the invoice has reached a final state and returns its status.</summary>
  protected Task<JsonElement> SettledAsync(
    Renewal renewal,
    TimeSpan? timeout = null
  )
  {
    return Eventually.UntilAsync(
      async () =>
      {
        // 404 until Billing has consumed InvoiceIssued: the saga row does not exist yet.
        var response = await System.Api.GetAsync($"/invoices/{renewal.InvoiceId}/renewal");
        if (!response.IsSuccessStatusCode) return (false, default);

        var status = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (Terminal.Contains(status.GetProperty("state").GetString()), status);
      },
      timeout ?? Patience,
      $"the renewal of invoice {renewal.InvoiceId} to reach a final state"
    );
  }

  protected static async Task<JsonElement> GetAsync(
    HttpClient client,
    string url
  )
  {
    var response = await client.GetAsync(url);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return await response.Content.ReadFromJsonAsync<JsonElement>();
  }

  /// <summary>Invoice and subscription changes follow the saga's final state by one more message hop.</summary>
  protected Task<JsonElement> InvoiceWithStatusAsync(
    Renewal renewal,
    string statusLabel
  )
  {
    return Eventually.UntilAsync(
      async () =>
      {
        var invoice = await GetAsync(System.Api, $"/invoices/{renewal.InvoiceId}");
        return (invoice.GetProperty("statusLabel").GetString() == statusLabel, invoice);
      },
      Patience,
      $"invoice {renewal.InvoiceId} to be {statusLabel}"
    );
  }

  protected Task<JsonElement> SubscriptionWithStatusAsync(
    Renewal renewal,
    int status
  )
  {
    return Eventually.UntilAsync(
      async () =>
      {
        var subscription = await GetAsync(System.Api, $"/subscriptions/{renewal.SubscriptionId}");
        return (subscription.GetProperty("status").GetInt32() == status, subscription);
      },
      Patience,
      $"subscription {renewal.SubscriptionId} to have status {status}"
    );
  }

  /// <summary>The event types of a payment's stream, once the stream has exactly the expected sequence.</summary>
  protected Task<JsonElement> PaymentEventsAsync(
    string paymentId,
    params string[] expected
  )
  {
    return Eventually.UntilAsync(
      async () =>
      {
        var response = await System.Payments.GetAsync($"/payments/{paymentId}/events");
        if (!response.IsSuccessStatusCode) return (false, default);

        var events = await response.Content.ReadFromJsonAsync<JsonElement>();
        var types = events.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("type").GetString());
        return (types.SequenceEqual(expected), events);
      },
      Patience,
      $"payment {paymentId} to have the events {string.Join(", ", expected)}"
    );
  }

  protected async Task<long> OutboxRowsAsync(
    string subscriptionId
  )
  {
    await using var connection = await System.OpenApiDbAsync();
    await using var command = new NpgsqlCommand(
      "SELECT count(*) FROM outbox_messages WHERE payload::jsonb ->> 'subscriptionId' = $1",
      connection
    );
    command.Parameters.AddWithValue(subscriptionId);
    return (long)(await command.ExecuteScalarAsync())!;
  }

  protected sealed record Renewal(
    string SubscriptionId,
    string InvoiceId,
    JsonElement Invoice
  );
}
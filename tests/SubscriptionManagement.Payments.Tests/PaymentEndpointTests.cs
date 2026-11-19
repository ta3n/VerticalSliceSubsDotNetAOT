using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SubscriptionManagement.Contracts;

namespace SubscriptionManagement.Payments.Tests;

/// <summary>The read side over HTTP: the folded payment and the raw stream it is folded from.</summary>
[Collection(PaymentsCollection.Name)]
public class PaymentEndpointTests(
  PaymentsFixture fixture
)
{
  [Fact]
  public async Task Get_payment_returns_the_snapshot()
  {
    var charge = new ChargePayment(
      Guid.NewGuid(),
      Guid.NewGuid(),
      Guid.NewGuid(),
      11.5m,
      "EUR",
      new DateOnly(2027, 1, 15)
    );
    await fixture.DeliverAsync(charge);

    var response = await fixture.Client.GetAsync($"/payments/{charge.PaymentId}");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(charge.PaymentId, body.GetProperty("id").GetGuid());
    Assert.Equal(charge.RenewalSagaId, body.GetProperty("invoiceId").GetGuid());
    Assert.Equal(charge.SubscriptionId, body.GetProperty("subscriptionId").GetGuid());
    Assert.Equal(11.5m, body.GetProperty("amount").GetDecimal());
    Assert.Equal("EUR", body.GetProperty("currency").GetString());
    Assert.Equal("2027-01-15", body.GetProperty("periodEnd").GetString());
    Assert.Equal("Captured", body.GetProperty("status").GetString());
    Assert.Equal(2, body.GetProperty("version").GetInt32());
  }

  [Fact]
  public async Task Get_payment_events_returns_the_stream_oldest_first()
  {
    var charge = new ChargePayment(
      Guid.NewGuid(),
      Guid.NewGuid(),
      Guid.NewGuid(),
      11.5m,
      "EUR",
      new DateOnly(2027, 1, 15)
    );
    await fixture.DeliverAsync(charge);
    await fixture.DeliverAsync(new CancelPayment(charge.PaymentId, "settlement-rejected"));

    var response = await fixture.Client.GetAsync($"/payments/{charge.PaymentId}/events");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")
      .EnumerateArray()
      .ToList();
    Assert.Equal(
      ["ChargeRequested", "ChargeCaptured", "ChargeRefunded"],
      items.Select(i => i.GetProperty("type").GetString())
    );
    Assert.Equal([1, 2, 3], items.Select(i => i.GetProperty("version").GetInt64()));
    Assert.All(
      items,
      i => Assert.True(i.GetProperty("timestamp").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5))
    );

    var requested = items[0].GetProperty("data");
    Assert.Equal(charge.RenewalSagaId, requested.GetProperty("invoiceId").GetGuid());
    Assert.Equal(charge.SubscriptionId, requested.GetProperty("subscriptionId").GetGuid());
    Assert.Equal(11.5m, requested.GetProperty("amount").GetDecimal());
    Assert.Equal("EUR", requested.GetProperty("currency").GetString());
    Assert.Equal("2027-01-15", requested.GetProperty("periodEnd").GetString());
    Assert.Equal("settlement-rejected", items[2].GetProperty("data").GetProperty("reason").GetString());
  }

  [Theory]
  [InlineData("/payments/{0}")]
  [InlineData("/payments/{0}/events")]
  public async Task Unknown_payment_is_a_404_problem(
    string path
  )
  {
    var id = Guid.NewGuid();

    var response = await fixture.Client.GetAsync(string.Format(path, id));

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("https://api.demo/errors/payment-not-found", body.GetProperty("type").GetString());
    Assert.Equal(404, body.GetProperty("status").GetInt32());
    Assert.Equal(id.ToString(), body.GetProperty("paymentId").GetString());
  }

  [Fact]
  public async Task Malformed_payment_id_does_not_reach_the_store()
  {
    var response = await fixture.Client.GetAsync("/payments/not-a-guid");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }
}
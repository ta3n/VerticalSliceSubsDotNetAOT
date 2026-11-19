using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubscriptionManagement.Billing.Features.CreditNotes;
using SubscriptionManagement.Contracts;
using Wolverine;

namespace SubscriptionManagement.Billing.Tests;

/// <summary>Last step of the cancel/refund choreography: PaymentRefunded becomes a credit note, exactly once.</summary>
[Collection(BillingCollection.Name)]
public class CreditNoteTests(
  BillingFixture fixture
)
{
  [Fact]
  public async Task PaymentRefunded_issues_a_credit_note_and_replies_to_nobody()
  {
    var refunded = NewRefund(11m, "subscription canceled");

    var before = DateTimeOffset.UtcNow;
    var session = await fixture.Track().InvokeMessageAndWaitAsync(refunded);

    Assert.Empty(session.Sent.AllMessages());
    var creditNote = Assert.Single(await ListAsync(refunded.SubscriptionId));
    Assert.NotEqual(Guid.Empty, creditNote.Id);
    Assert.Equal(refunded.PaymentId, creditNote.PaymentId);
    Assert.Equal(refunded.InvoiceId, creditNote.InvoiceId);
    Assert.Equal(refunded.SubscriptionId, creditNote.SubscriptionId);
    Assert.Equal(11m, creditNote.Amount);
    Assert.Equal("USD", creditNote.Currency);
    Assert.Equal("subscription canceled", creditNote.Reason);
    Assert.InRange(creditNote.IssuedAt, before.AddMilliseconds(-1), DateTimeOffset.UtcNow);
  }

  [Fact]
  public async Task A_repeated_PaymentRefunded_keeps_the_first_credit_note()
  {
    var refunded = NewRefund(11m, "subscription canceled");
    await fixture.Track().InvokeMessageAndWaitAsync(refunded);
    var first = Assert.Single(await ListAsync(refunded.SubscriptionId));

    // Same payment, delivered again with different details: still the same refund.
    await fixture.Track().InvokeMessageAndWaitAsync(refunded with { Amount = 99m, Reason = "duplicate" });

    Assert.Equal(first, Assert.Single(await ListAsync(refunded.SubscriptionId)));
  }

  [Fact]
  public async Task Concurrent_duplicates_all_succeed_and_leave_one_credit_note()
  {
    var refunded = NewRefund(11m, "subscription canceled");

    // Each delivery on its own message bus, as redeliveries arriving on parallel listeners would be.
    var failures = new List<Exception>();
    await fixture.Track()
      .ExecuteAndWaitAsync(
        _ => Task.WhenAll(
          Enumerable.Range(0, 8)
            .Select(
              _ => Task.Run(
                async () =>
                {
                  try
                  {
                    await using var scope = fixture.App.Services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                      .InvokeAsync(refunded);
                  }
                  catch (Exception e)
                  {
                    lock (failures)
                    {
                      failures.Add(e);
                    }
                  }
                }
              )
            )
        )
      );

    Assert.Empty(failures);
    Assert.Single(await ListAsync(refunded.SubscriptionId));
  }

  [Fact]
  public async Task The_database_refuses_a_second_credit_note_for_a_payment()
  {
    var refunded = NewRefund(11m, "subscription canceled");
    await fixture.Track().InvokeMessageAndWaitAsync(refunded);

    // A plain insert, without the handler's ON CONFLICT clause.
    var failure = await Assert.ThrowsAsync<DbUpdateException>(
      () => fixture.QueryAsync(
        async db =>
        {
          db.CreditNotes.Add(CreditNote.Issue(refunded, DateTimeOffset.UtcNow));
          return await db.SaveChangesAsync();
        }
      )
    );

    Assert.Contains("ux_credit_notes_payment_id", failure.InnerException!.Message);
    Assert.Single(await ListAsync(refunded.SubscriptionId));
  }

  [Fact]
  public async Task Credit_notes_are_listed_per_subscription_newest_first()
  {
    var subscriptionId = Guid.NewGuid();
    var older = NewRefund(5m, "first") with { SubscriptionId = subscriptionId };
    var newer = NewRefund(7.5m, "second") with { SubscriptionId = subscriptionId };
    await fixture.Track().InvokeMessageAndWaitAsync(older);
    await fixture.Track().InvokeMessageAndWaitAsync(newer);
    await fixture.Track().InvokeMessageAndWaitAsync(NewRefund(1m, "another subscription"));

    var response = await fixture.Client.GetAsync($"/credit-notes?subscriptionId={subscriptionId}");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    var list = await response.Content.ReadFromJsonAsync<CreditNoteList>();
    Assert.Equal([newer.PaymentId, older.PaymentId], list!.Items.Select(x => x.PaymentId));
    Assert.Equal([7.5m, 5m], list.Items.Select(x => x.Amount));
    Assert.All(list.Items, x => Assert.Equal(subscriptionId, x.SubscriptionId));
  }

  [Fact]
  public async Task A_subscription_without_refunds_has_an_empty_list()
  {
    var body = await fixture.Client.GetStringAsync($"/credit-notes?subscriptionId={Guid.NewGuid()}");

    Assert.Equal("""{"items":[]}""", body);
  }

  [Theory]
  [InlineData("/credit-notes")]
  [InlineData("/credit-notes?subscriptionId=not-a-uuid")]
  public async Task A_missing_or_malformed_subscription_id_is_a_bad_request(
    string path
  )
  {
    var response = await fixture.Client.GetAsync(path);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  private static PaymentRefunded NewRefund(
    decimal amount,
    string reason
  )
  {
    return new PaymentRefunded(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount, "USD", reason);
  }

  private async Task<IReadOnlyList<CreditNoteResponse>> ListAsync(
    Guid subscriptionId
  )
  {
    return (await fixture.Client.GetFromJsonAsync<CreditNoteList>($"/credit-notes?subscriptionId={subscriptionId}"))
      !.Items;
  }
}
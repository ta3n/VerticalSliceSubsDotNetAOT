using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubscriptionManagement.Billing.Features.CreditNotes;
using SubscriptionManagement.Billing.Infrastructure;

namespace SubscriptionManagement.Billing.Tests;

/// <summary>
/// Billing's <see cref="BillingDbContext"/> is leased from a pool (<c>AddDbContextPool</c>) on top of the options
/// <c>AddDbContextWithWolverineIntegration</c> registers, so nothing Wolverine wires into the context may be lost.
/// The pool hands out its idle instances first-in first-out, so a scope does not necessarily get back the instance the
/// previous one returned; these tests look at how many different instances many scopes see.
/// </summary>
[Collection(BillingCollection.Name)]
public class DbContextPoolingTests(
  BillingFixture fixture
)
{
  private const int Scopes = 50;

  [Fact]
  public async Task Sequential_scopes_reuse_pooled_instances()
  {
    var seen = new HashSet<BillingDbContext>(ReferenceEqualityComparer.Instance);
    for (var i = 0; i < Scopes; i++)
    {
      await using var scope = fixture.App.Services.CreateAsyncScope();
      seen.Add(scope.ServiceProvider.GetRequiredService<BillingDbContext>());
    }

    // Without pooling every scope builds its own context: Scopes different instances. The pool may already hold
    // several idle ones (earlier tests ran handlers in parallel), but each is leased more than once.
    Assert.True(seen.Count < Scopes, $"{seen.Count} different contexts in {Scopes} scopes: nothing was reused.");
  }

  [Fact]
  public async Task A_reused_context_starts_clean()
  {
    BillingDbContext dirty;
    await using (var scope = fixture.App.Services.CreateAsyncScope())
    {
      dirty = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
      dirty.CreditNotes.Add(
        new()
        {
          Id = Guid.NewGuid(),
          PaymentId = Guid.NewGuid(),
          InvoiceId = Guid.NewGuid(),
          SubscriptionId = Guid.NewGuid(),
          Amount = 1m,
          Currency = "USD",
          Reason = "pooling",
          IssuedAt = DateTimeOffset.UtcNow
        }
      );
      Assert.Single(dirty.ChangeTracker.Entries<CreditNote>());
      // Disposed without SaveChanges: the pending insert must not leak into the next lease.
    }

    // The idle instances come back in turn, so the dirty one is leased again within a few scopes.
    for (var i = 0; i < Scopes; i++)
    {
      await using var scope = fixture.App.Services.CreateAsyncScope();
      var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
      Assert.Empty(db.ChangeTracker.Entries());
      if (ReferenceEquals(db, dirty)) return;
    }

    Assert.Fail("The context was never leased again: it is not pooled.");
  }

  [Fact]
  public async Task The_pooled_context_keeps_the_Wolverine_model()
  {
    await using var scope = fixture.App.Services.CreateAsyncScope();
    var model = scope.ServiceProvider.GetRequiredService<BillingDbContext>().Model;

    // Mapped by WolverineModelCustomizer, which only runs when the pool is built on Wolverine's own options.
    Assert.Contains(model.GetEntityTypes(), entity => entity.GetSchema() == "wolverine");
  }
}
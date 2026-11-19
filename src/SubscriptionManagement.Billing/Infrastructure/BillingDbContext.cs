using Microsoft.EntityFrameworkCore;
using SubscriptionManagement.Billing.Features.CreditNotes;
using SubscriptionManagement.Billing.Features.Renewals;

namespace SubscriptionManagement.Billing.Infrastructure;

/// <summary>
/// Billing's tables in the <c>billing</c> database. There are no EF migrations: Wolverine creates and updates the
/// mapped tables at startup, next to its own envelope tables (see <see cref="ServiceSetup"/>).
/// </summary>
public sealed class BillingDbContext(
  DbContextOptions<BillingDbContext> options
) : DbContext(options)
{
  public DbSet<RenewalSaga> RenewalSagas => Set<RenewalSaga>();
  public DbSet<CreditNote> CreditNotes => Set<CreditNote>();

  protected override void OnModelCreating(
    ModelBuilder modelBuilder
  )
  {
    modelBuilder.Entity<RenewalSaga>(
      b =>
      {
        b.ToTable("renewal_sagas");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.SubscriptionId).HasColumnName("subscription_id");
        b.Property(x => x.PaymentId).HasColumnName("payment_id");
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(20, 4);
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.FailureReason).HasColumnName("failure_reason");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        // Wolverine increments Version when it saves a changed saga; as the concurrency token it turns that save
        // into UPDATE ... WHERE version = <the value read>, so a concurrent change is detected instead of lost.
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
      }
    );

    modelBuilder.Entity<CreditNote>(
      b =>
      {
        b.ToTable("credit_notes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.PaymentId).HasColumnName("payment_id");
        b.Property(x => x.InvoiceId).HasColumnName("invoice_id");
        b.Property(x => x.SubscriptionId).HasColumnName("subscription_id");
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(20, 4);
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        b.Property(x => x.Reason).HasColumnName("reason");
        b.Property(x => x.IssuedAt).HasColumnName("issued_at");
        b.HasIndex(x => x.PaymentId).IsUnique().HasDatabaseName("ux_credit_notes_payment_id");
        b.HasIndex(x => x.SubscriptionId).HasDatabaseName("ix_credit_notes_subscription_id");
      }
    );
  }
}
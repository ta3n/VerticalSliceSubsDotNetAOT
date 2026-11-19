using Microsoft.EntityFrameworkCore;
using SubscriptionManagement.Billing.Infrastructure;
using SubscriptionManagement.Contracts;

namespace SubscriptionManagement.Billing.Features.CreditNotes.IssueCreditNote;

/// <summary>
/// Last step of the cancel/refund choreography: Payments announces a refund, Billing documents it. Nobody waits for
/// the result, so there is no reply.
/// </summary>
public static class IssueCreditNoteHandler
{
  /// <summary>
  /// One statement, so a payment that already has a credit note is a no-op however the deliveries interleave: a
  /// check followed by an insert would let the second of two concurrent duplicates fail on the unique index.
  /// </summary>
  public static async Task Handle(
    PaymentRefunded refunded,
    BillingDbContext db,
    TimeProvider time,
    CancellationToken ct
  )
  {
    var creditNote = CreditNote.Issue(refunded, time.GetUtcNow());

    await db.Database.ExecuteSqlAsync(
      $"""
      INSERT INTO credit_notes (id, payment_id, invoice_id, subscription_id, amount, currency, reason, issued_at)
      VALUES ({creditNote.Id}, {creditNote.PaymentId}, {creditNote.InvoiceId}, {creditNote.SubscriptionId},
              {creditNote.Amount}, {creditNote.Currency}, {creditNote.Reason}, {creditNote.IssuedAt})
      ON CONFLICT (payment_id) DO NOTHING
      """,
      ct
    );
  }
}
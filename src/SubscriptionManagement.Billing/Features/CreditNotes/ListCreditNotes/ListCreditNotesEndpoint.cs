using Microsoft.EntityFrameworkCore;
using SubscriptionManagement.Billing.Infrastructure;

namespace SubscriptionManagement.Billing.Features.CreditNotes.ListCreditNotes;

public static class ListCreditNotesEndpoint
{
    /// <summary>Newest first. A subscription without refunds is an empty list: Billing does not know subscriptions.</summary>
    public static void Map(
        IEndpointRouteBuilder app
    )
    {
        app.MapGet(
            "/credit-notes",
            async (Guid subscriptionId, BillingDbContext db, CancellationToken ct) =>
            {
                var creditNotes = await db.CreditNotes.AsNoTracking()
                    .Where(x => x.SubscriptionId == subscriptionId)
                    .OrderByDescending(x => x.IssuedAt)
                    .ThenBy(x => x.Id)
                    .ToListAsync(ct);
                return TypedResults.Ok(new CreditNoteList(creditNotes.Select(CreditNoteResponse.From).ToList()));
            }
        );
    }
}
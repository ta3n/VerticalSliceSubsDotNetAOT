using System.Diagnostics;
using Npgsql;
using SubscriptionManagement.Infrastructure;
using SubscriptionManagement.Infrastructure.Billing;

namespace SubscriptionManagement.Features.Subscriptions.StartRenewal;

public static class StartRenewalEndpoint
{
    public static void Map(
        IEndpointRouteBuilder app
    )
    {
        app.MapPost(
            "/subscriptions/{id}/renewals",
            async (
                Guid id, NpgsqlDataSource db, SubscriptionCache cache, IBillingGateway billing, OutboxOptions outbox,
                TimeProvider time, CancellationToken ct
            ) =>
            {
                Activity.Current?.SetTag("subscription.id", id.ToString());
                var result = await StartRenewalHandler.Handle(
                    new StartRenewalCommand(id),
                    db,
                    cache,
                    billing,
                    outbox,
                    time,
                    ct
                );
                Activity.Current?.SetTag("invoice.id", result.Invoice.Id.ToString());
                // Accepted, not OK: the invoice is collected asynchronously; its progress is at the Location.
                return TypedResults.Accepted($"/invoices/{result.Invoice.Id}/renewal", result);
            }
        );
    }
}
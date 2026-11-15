using Marten;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SubscriptionManagement.Payments.Features.Payments.GetPaymentEvents;

/// <summary><c>Type</c> is the event's .NET type name (for example <c>ChargeCaptured</c>), <c>Data</c> its body.</summary>
public sealed record PaymentEventResponse(
    long Version,
    string Type,
    object Data,
    DateTimeOffset Timestamp
);

public sealed record PaymentEventList(
    IReadOnlyList<PaymentEventResponse> Items
);

public static class GetPaymentEventsEndpoint
{
    /// <summary>The stream itself, oldest first: the source of truth that <c>GET /payments/{id}</c> is folded from.</summary>
    public static void Map(
        IEndpointRouteBuilder app
    )
    {
        app.MapGet(
            "/payments/{id}/events",
            async Task<Results<Ok<PaymentEventList>, ProblemHttpResult>> (
                Guid id, IQuerySession session, CancellationToken ct
            ) =>
            {
                var events = await session.Events.FetchStreamAsync(id, token: ct);
                if (events.Count == 0) return PaymentProblems.NotFound(id);

                return TypedResults.Ok(
                    new PaymentEventList(
                        [
                            .. events.Select(
                                e => new PaymentEventResponse(e.Version, e.Data.GetType().Name, e.Data, e.Timestamp)
                            )
                        ]
                    )
                );
            }
        );
    }
}
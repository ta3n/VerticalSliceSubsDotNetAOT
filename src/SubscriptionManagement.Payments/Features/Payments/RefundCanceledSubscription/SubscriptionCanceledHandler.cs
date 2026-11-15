using Marten;
using SubscriptionManagement.Contracts;

namespace SubscriptionManagement.Payments.Features.Payments.RefundCanceledSubscription;

public static class SubscriptionCanceledHandler
{
    /// <summary>
    /// Choreography: reacts to the API's fact on its own and announces each refund as a <see cref="PaymentRefunded"/>
    /// fact. All refunds of one cancellation commit together.
    /// </summary>
    public static async Task<IEnumerable<object>> Handle(
        SubscriptionCanceled fact,
        IDocumentSession session,
        CancellationToken ct
    )
    {
        // The snapshots only find the candidates; each decision is taken on the stream, under its version check.
        var paymentIds = await session.Query<Payment>()
            .Where(p => p.SubscriptionId == fact.SubscriptionId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var messages = new List<object>();
        foreach (var paymentId in paymentIds)
        {
            var stream = await session.Events.FetchForWriting<Payment>(paymentId, ct);
            var decision = PaymentDecider.Decide(stream.Aggregate!, fact);
            stream.AppendMany(decision.Events);
            messages.AddRange(decision.Messages);
        }

        return messages;
    }
}
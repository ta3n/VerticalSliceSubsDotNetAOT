using Marten;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Payments.Infrastructure;

namespace SubscriptionManagement.Payments.Features.Payments.Charge;

public static class ChargePaymentHandler
{
    /// <summary>
    /// Wolverine's Marten middleware saves the session after this returns: the appended events, the snapshot and the
    /// returned messages (outbox) commit in one transaction, guarded by the stream version read here.
    /// </summary>
    public static async Task<IEnumerable<object>> Handle(
        ChargePayment command,
        IDocumentSession session,
        GatewayOptions gateway,
        CancellationToken ct
    )
    {
        var stream = await session.Events.FetchForWriting<Payment>(command.PaymentId, ct);
        var decision = PaymentDecider.Decide(stream.Aggregate, command, gateway);
        stream.AppendMany(decision.Events);
        return decision.Messages;
    }
}
using Marten;
using SubscriptionManagement.Contracts;

namespace SubscriptionManagement.Payments.Features.Payments.Cancel;

public static class CancelPaymentHandler
{
  /// <summary>Compensation from the renewal saga: cancels an uncaptured charge, refunds a captured one.</summary>
  public static async Task<IEnumerable<object>> Handle(
    CancelPayment command,
    IDocumentSession session,
    CancellationToken ct
  )
  {
    var stream = await session.Events.FetchForWriting<Payment>(command.PaymentId, ct);
    var decision = PaymentDecider.Decide(stream.Aggregate, command);
    stream.AppendMany(decision.Events);
    return decision.Messages;
  }
}
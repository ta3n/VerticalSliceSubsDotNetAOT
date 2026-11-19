using SubscriptionManagement.Contracts;
using SubscriptionManagement.Payments.Infrastructure;

namespace SubscriptionManagement.Payments.Features.Payments;

/// <summary>What a command does to a payment: events to append to its stream and messages to publish with them.</summary>
public sealed record Decision(
  IReadOnlyList<object> Events,
  IReadOnlyList<object> Messages
)
{
  public static readonly Decision None = new([], []);
}

/// <summary>
/// All payment rules, as pure functions of the current state (null: the stream does not exist) and a message. No I/O:
/// the handlers load the state, call this, append the events and return the messages.
/// </summary>
public static class PaymentDecider
{
  public const string CardDeclined = "card-declined";
  public const string SubscriptionCanceledReason = "subscription-canceled";

  /// <summary>The simulated gateway. The amount picks the outcome (see <see cref="GatewayOptions"/>).</summary>
  public static Decision Decide(
    Payment? state,
    ChargePayment command,
    GatewayOptions gateway
  )
  {
    // Any existing stream makes this a no-op: a redelivered charge, or a charge overtaken by its cancel.
    if (state is not null) return Decision.None;

    var requested = new ChargeRequested(
      command.RenewalSagaId,
      command.SubscriptionId,
      command.Amount,
      command.Currency,
      command.PeriodEnd
    );

    if (command.Amount >= gateway.NoReplyAtOrAbove) return new([requested], []);

    if (command.Amount >= gateway.DeclineAtOrAbove)
      return new(
        [requested, new ChargeDeclined(CardDeclined)],
        [new PaymentFailed(command.RenewalSagaId, command.PaymentId, CardDeclined)]
      );

    return new(
      [requested, new ChargeCaptured()],
      [new PaymentSucceeded(command.RenewalSagaId, command.PaymentId)]
    );
  }

  /// <summary>
  /// Before a capture the charge is canceled (also when it has not arrived yet, which blocks it for good); after a
  /// capture the money is refunded. A declined, canceled or refunded payment is left alone.
  /// </summary>
  public static Decision Decide(
    Payment? state,
    CancelPayment command
  )
  {
    return state?.Status switch
    {
      null or PaymentStatus.Requested => new([new ChargeCanceled(command.Reason)], []),
      PaymentStatus.Captured => Refund(state, command.Reason),
      _ => Decision.None
    };
  }

  /// <summary>A canceled subscription gets back every captured payment for a period that has not ended yet.</summary>
  public static Decision Decide(
    Payment state,
    SubscriptionCanceled fact
  )
  {
    return state.Status == PaymentStatus.Captured && state.PeriodEnd > fact.CanceledOn
      ? Refund(state, SubscriptionCanceledReason)
      : Decision.None;
  }

  private static Decision Refund(
    Payment state,
    string reason
  )
  {
    return new Decision(
      [new ChargeRefunded(reason)],
      [new PaymentRefunded(state.Id, state.InvoiceId, state.SubscriptionId, state.Amount, state.Currency, reason)]
    );
  }
}
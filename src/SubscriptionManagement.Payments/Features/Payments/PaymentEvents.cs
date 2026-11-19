namespace SubscriptionManagement.Payments.Features.Payments;

// The events of a payment stream, in the order a stream can contain them:
//   ChargeRequested, then at most one of ChargeCaptured | ChargeDeclined | ChargeCanceled; ChargeRefunded only after
//   ChargeCaptured. A stream may also consist of a single ChargeCanceled (cancel before charge).

public sealed record ChargeRequested(
  Guid InvoiceId,
  Guid SubscriptionId,
  decimal Amount,
  string Currency,
  DateOnly PeriodEnd
);

public sealed record ChargeCaptured;

public sealed record ChargeDeclined(
  string Reason
);

public sealed record ChargeCanceled(
  string Reason
);

public sealed record ChargeRefunded(
  string Reason
);
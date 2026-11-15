namespace SubscriptionManagement.Contracts;

// Messages exchanged over the broker. Each type has exactly one consuming service (named on the right): conventional
// routing gives every type its own exchange and queue, so a second consumer would compete for the same queue.
// "RenewalSagaId" is the invoice id; Wolverine finds the saga through that member name.

// API outbox -> Relay -> broker (facts)
public sealed record InvoiceIssued(
    Guid InvoiceId,
    Guid SubscriptionId,
    decimal Amount,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd
); // -> billing

public sealed record SubscriptionCanceled(
    Guid SubscriptionId,
    DateOnly CanceledOn
); // -> payments

// billing -> payments (commands)
public sealed record ChargePayment(
    Guid RenewalSagaId,
    Guid PaymentId,
    Guid SubscriptionId,
    decimal Amount,
    string Currency,
    DateOnly PeriodEnd
);

public sealed record CancelPayment(
    Guid PaymentId,
    string Reason
);

// payments -> billing (replies)
public sealed record PaymentSucceeded(
    Guid RenewalSagaId,
    Guid PaymentId
);

public sealed record PaymentFailed(
    Guid RenewalSagaId,
    Guid PaymentId,
    string Reason
);

// billing -> relay (commands)
public sealed record SettleInvoice(
    Guid RenewalSagaId,
    Guid PaymentId
);

public sealed record CancelRenewal(
    Guid InvoiceId,
    Guid SubscriptionId,
    string Reason
);

// relay -> billing (replies)
public sealed record InvoiceSettled(
    Guid RenewalSagaId
);

public sealed record InvoiceSettlementRejected(
    Guid RenewalSagaId,
    string Reason
);

// payments -> billing (fact)
public sealed record PaymentRefunded(
    Guid PaymentId,
    Guid InvoiceId,
    Guid SubscriptionId,
    decimal Amount,
    string Currency,
    string Reason
);
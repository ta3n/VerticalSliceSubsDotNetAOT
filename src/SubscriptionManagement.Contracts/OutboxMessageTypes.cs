namespace SubscriptionManagement.Contracts;

/// <summary>
/// Values of <c>outbox_messages.type</c>: the messages the API writes to its outbox table and the Relay publishes.
/// The payload is the record serialized as JSON with the web defaults (camelCase).
/// </summary>
public static class OutboxMessageTypes
{
  public const string InvoiceIssued = nameof(Contracts.InvoiceIssued);
  public const string SubscriptionCanceled = nameof(Contracts.SubscriptionCanceled);
}
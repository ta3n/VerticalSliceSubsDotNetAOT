namespace SubscriptionManagement.Contracts;

/// <summary>
/// The Kafka side of the messaging. RabbitMQ carries commands and replies with exactly one consuming service each;
/// Kafka carries a durable, replayable stream of business facts that any number of services read independently.
/// </summary>
public static class KafkaTopics
{
  /// <summary>
  /// Copies of the facts <see cref="InvoiceIssued" />, <see cref="SubscriptionCanceled" />,
  /// <see cref="PaymentSucceeded" />, <see cref="PaymentFailed" /> and <see cref="PaymentRefunded" />, published by
  /// the Relay and Payments next to their RabbitMQ route and read by Reporting.
  /// </summary>
  public const string SubscriptionEvents = "subscription-events";

  /// <summary>Name of the connection (<c>ConnectionStrings:kafka</c>), i.e. of the AppHost resource.</summary>
  public const string ConnectionName = "kafka";
}
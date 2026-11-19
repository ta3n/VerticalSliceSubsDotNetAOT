namespace SubscriptionManagement.Relay.Infrastructure;

/// <summary>Pace of the outbox drain.</summary>
public sealed class RelayOptions
{
  public const string SectionName = "Relay";

  /// <summary>Pause between two polls of <c>outbox_messages</c> once the table has been emptied.</summary>
  public int PollIntervalMs { get; set; } = 250;

  /// <summary>Rows relayed per transaction. A full batch is followed by the next one without a pause.</summary>
  public int BatchSize { get; set; } = 100;
}
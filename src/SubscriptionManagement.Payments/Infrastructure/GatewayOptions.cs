namespace SubscriptionManagement.Payments.Infrastructure;

/// <summary>
/// Thresholds of the simulated payment gateway: the charged amount alone decides the outcome, so a caller (k6, a test)
/// picks capture, decline or silence through the price.
/// </summary>
public sealed class GatewayOptions
{
  public const string SectionName = "Gateway";

  /// <summary>Amounts at or above this are declined.</summary>
  public decimal DeclineAtOrAbove { get; set; } = 5000m;

  /// <summary>Amounts at or above this get no answer at all, which is what the renewal saga's timeout is for.</summary>
  public decimal NoReplyAtOrAbove { get; set; } = 9000m;
}
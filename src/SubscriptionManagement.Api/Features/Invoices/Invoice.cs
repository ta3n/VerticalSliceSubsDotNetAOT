using SubscriptionManagement.Features.Subscriptions;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Invoices;

/// <summary>Numeric values are the wire contract.</summary>
public enum InvoiceStatus
{
  Open = 1,
  Paid = 2,
  Void = 3
}

public static class InvoiceStatusNames
{
  public static string ToDb(
    this InvoiceStatus status
  )
  {
    return status switch
    {
      InvoiceStatus.Open => "OPEN",
      InvoiceStatus.Paid => "PAID",
      InvoiceStatus.Void => "VOID",
      _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
  }

  public static InvoiceStatus FromDb(
    string value
  )
  {
    return value switch
    {
      "OPEN" => InvoiceStatus.Open,
      "PAID" => InvoiceStatus.Paid,
      "VOID" => InvoiceStatus.Void,
      _ => throw new InvalidOperationException($"Unknown invoice status '{value}' in database.")
    };
  }
}

public static class InvoiceProblems
{
  public static ApiProblemException NotFound(
    Guid id
  )
  {
    return new ApiProblemException(
      StatusCodes.Status404NotFound,
      "invoice-not-found",
      "Invoice not found",
      $"Invoice {id} was not found.",
      new Dictionary<string, object> { ["invoiceId"] = id.ToString() }
    );
  }

  public static ApiProblemException NotPayable(
    Guid id,
    InvoiceStatus status
  )
  {
    return new ApiProblemException(
      StatusCodes.Status409Conflict,
      "invoice-not-payable",
      "Invoice is not payable",
      $"Invoice {id} is {status.ToDb()}.",
      new Dictionary<string, object> { ["invoiceId"] = id.ToString(), ["invoiceStatus"] = status.ToDb() }
    );
  }

  public static ApiProblemException NotVoidable(
    Guid id,
    InvoiceStatus status
  )
  {
    return new ApiProblemException(
      StatusCodes.Status409Conflict,
      "invoice-not-voidable",
      "Invoice is not voidable",
      $"Invoice {id} is {status.ToDb()}.",
      new Dictionary<string, object> { ["invoiceId"] = id.ToString(), ["invoiceStatus"] = status.ToDb() }
    );
  }
}

/// <summary>Billing document issued when a subscription renews a paid period.</summary>
public sealed class Invoice
{
  private Invoice(
    Guid id,
    Guid subscriptionId,
    decimal amount,
    string currency,
    DateOnly periodStart,
    DateOnly periodEnd,
    InvoiceStatus status,
    DateTime issuedAt,
    DateTime? paidAt,
    Guid? paymentId
  )
  {
    Id = id;
    SubscriptionId = subscriptionId;
    Amount = amount;
    Currency = currency;
    PeriodStart = periodStart;
    PeriodEnd = periodEnd;
    Status = status;
    IssuedAt = issuedAt;
    PaidAt = paidAt;
    PaymentId = paymentId;
  }

  public Guid Id { get; }
  public Guid SubscriptionId { get; }
  public decimal Amount { get; }
  public string Currency { get; }
  public DateOnly PeriodStart { get; }
  public DateOnly PeriodEnd { get; }
  public InvoiceStatus Status { get; private set; }
  public DateTime IssuedAt { get; }
  public DateTime? PaidAt { get; private set; }

  // The payment that settled the invoice (set by Settle only); not part of the public response.
  public Guid? PaymentId { get; private set; }

  public static Invoice Issue(
    Guid subscriptionId,
    decimal amount,
    string currency,
    BilledPeriod period,
    DateTime nowUtc
  )
  {
    return new Invoice(
      Guid.NewGuid(),
      subscriptionId,
      // Match NUMERIC(20,4) (PostgreSQL rounds half away from zero) so the response equals what is stored.
      decimal.Round(amount, 4, MidpointRounding.AwayFromZero),
      currency,
      period.Start,
      period.End,
      InvoiceStatus.Open,
      Truncate(nowUtc),
      null,
      null
    );
  }

  public static Invoice Rehydrate(
    Guid id,
    Guid subscriptionId,
    decimal amount,
    string currency,
    DateOnly periodStart,
    DateOnly periodEnd,
    InvoiceStatus status,
    DateTime issuedAt,
    DateTime? paidAt,
    Guid? paymentId = null
  )
  {
    return new Invoice(
      id,
      subscriptionId,
      amount,
      currency,
      periodStart,
      periodEnd,
      status,
      issuedAt,
      paidAt,
      paymentId
    );
  }

  /// <summary>A paid or void invoice cannot be paid again.</summary>
  public void Pay(
    DateTime nowUtc
  )
  {
    if (Status != InvoiceStatus.Open) throw InvoiceProblems.NotPayable(Id, Status);

    Status = InvoiceStatus.Paid;
    PaidAt = Truncate(nowUtc);
  }

  /// <summary>
  /// Marks the invoice paid by a collected payment. Repeating the call with the same payment changes nothing, so a
  /// redelivered settle command is harmless; any other paid or void invoice is a conflict.
  /// </summary>
  public void Settle(
    Guid paymentId,
    DateTime nowUtc
  )
  {
    if (Status == InvoiceStatus.Paid && PaymentId == paymentId) return;

    if (Status != InvoiceStatus.Open) throw InvoiceProblems.NotPayable(Id, Status);

    Status = InvoiceStatus.Paid;
    PaidAt = Truncate(nowUtc);
    PaymentId = paymentId;
  }

  /// <summary>Cancels an unpaid invoice. Voiding a void invoice changes nothing; a paid invoice cannot be voided.</summary>
  public void Void()
  {
    if (Status == InvoiceStatus.Paid) throw InvoiceProblems.NotVoidable(Id, Status);

    Status = InvoiceStatus.Void;
  }

  // PostgreSQL timestamps keep microseconds; .NET ticks are finer.
  private static DateTime Truncate(
    DateTime value
  )
  {
    return new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
  }
}
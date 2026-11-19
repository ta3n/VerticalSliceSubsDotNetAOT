namespace SubscriptionManagement.Infrastructure.Billing;

public static class BillingProblems
{
  public static ApiProblemException NotConfigured()
  {
    return new ApiProblemException(
      StatusCodes.Status503ServiceUnavailable,
      "billing-not-configured",
      "Billing is not configured",
      "Renewal collection needs Billing:GrpcAddress and Outbox:Enabled."
    );
  }

  public static ApiProblemException QuoteUnavailable()
  {
    return new ApiProblemException(
      StatusCodes.Status503ServiceUnavailable,
      "quote-unavailable",
      "Renewal quote unavailable",
      "Billing did not return a renewal quote in time."
    );
  }

  public static ApiProblemException Unavailable()
  {
    return new ApiProblemException(
      StatusCodes.Status503ServiceUnavailable,
      "billing-unavailable",
      "Billing unavailable",
      "Billing did not answer in time."
    );
  }

  public static ApiProblemException RenewalNotFound(
    Guid invoiceId
  )
  {
    return new ApiProblemException(
      StatusCodes.Status404NotFound,
      "renewal-not-found",
      "Renewal not found",
      $"Billing has no renewal for invoice {invoiceId}.",
      new Dictionary<string, object> { ["invoiceId"] = invoiceId.ToString() }
    );
  }
}
using System.Globalization;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using SubscriptionManagement.Billing.Infrastructure;
using SubscriptionManagement.Contracts.Grpc;

namespace SubscriptionManagement.Billing.Features.Renewals;

/// <summary>
/// gRPC surface of Billing (billing.proto), called by the API. Decimals travel as invariant-culture strings and dates
/// as <c>yyyy-MM-dd</c>; a value that does not parse is INVALID_ARGUMENT.
/// </summary>
public sealed class BillingGrpcService(
  BillingOptions options,
  BillingDbContext db
) : BillingService.BillingServiceBase
{
  private const string DateFormat = "yyyy-MM-dd";

  /// <summary>Stateless: the same request always gets the same reply, so the caller may retry it.</summary>
  public override Task<RenewalQuoteReply> GetRenewalQuote(
    RenewalQuoteRequest request,
    ServerCallContext context
  )
  {
    if (!Guid.TryParse(request.SubscriptionId, out _)) throw Invalid("subscription_id must be a UUID.");

    if (!decimal.TryParse(
        request.Price,
        NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture,
        out var price
      ))
      throw Invalid("price must be a non-negative decimal.");

    if (string.IsNullOrWhiteSpace(request.Currency)) throw Invalid("currency is required.");

    if (!TryParseDate(request.PeriodStart, out var periodStart)
      || !TryParseDate(request.PeriodEnd, out var periodEnd)
      || periodEnd <= periodStart)
      throw Invalid(
        $"period_start and period_end must be {DateFormat} dates, with period_end after period_start."
      );

    RenewalQuote quote;
    try
    {
      quote = RenewalQuote.For(price, options.TaxRate);
    }
    catch (OverflowException)
    {
      throw Invalid("price is out of range.");
    }

    return Task.FromResult(
      new RenewalQuoteReply
      {
        Net = Money(quote.Net),
        Tax = Money(quote.Tax),
        Total = Money(quote.Total),
        TaxRate = quote.TaxRate.ToString(CultureInfo.InvariantCulture)
      }
    );
  }

  /// <summary>The saga row of an invoice. NOT_FOUND until Billing has consumed its InvoiceIssued.</summary>
  public override async Task<RenewalReply> GetRenewal(
    GetRenewalRequest request,
    ServerCallContext context
  )
  {
    if (!Guid.TryParse(request.InvoiceId, out var invoiceId)) throw Invalid("invoice_id must be a UUID.");

    var saga = await db.RenewalSagas.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == invoiceId, context.CancellationToken)
      ?? throw new RpcException(new Status(StatusCode.NotFound, $"No renewal for invoice {invoiceId}."));

    return new RenewalReply
    {
      InvoiceId = saga.Id.ToString(),
      SubscriptionId = saga.SubscriptionId.ToString(),
      PaymentId = saga.PaymentId.ToString(),
      State = saga.State.ToString(),
      FailureReason = saga.FailureReason ?? "",
      UpdatedAt = saga.UpdatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
    };
  }

  private static bool TryParseDate(
    string value,
    out DateOnly date
  )
  {
    return DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
  }

  private static string Money(
    decimal value
  )
  {
    return value.ToString("F4", CultureInfo.InvariantCulture);
  }

  private static RpcException Invalid(
    string detail
  )
  {
    return new RpcException(new Status(StatusCode.InvalidArgument, detail));
  }
}
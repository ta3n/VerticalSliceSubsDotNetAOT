using System.Globalization;
using Grpc.Core;
using SubscriptionManagement.Contracts.Grpc;
using SubscriptionManagement.Features.Subscriptions;

namespace SubscriptionManagement.Infrastructure.Billing;

/// <summary>
/// Calls Billing over gRPC with the generated client. On the wire decimals are invariant-culture strings, dates
/// <c>yyyy-MM-dd</c> and timestamps ISO 8601 (see billing.proto).
/// </summary>
public sealed class GrpcBillingGateway(
    BillingService.BillingServiceClient client
) : IBillingGateway
{
    // Covers all attempts of a call (the channel retries Unavailable), so a request never waits longer than this.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    public async Task<RenewalQuote> GetRenewalQuoteAsync(
        Guid subscriptionId,
        decimal price,
        string currency,
        BilledPeriod period,
        CancellationToken ct
    )
    {
        var request = new RenewalQuoteRequest
        {
            SubscriptionId = subscriptionId.ToString(),
            Price = price.ToString(CultureInfo.InvariantCulture),
            Currency = currency,
            PeriodStart = period.Start.ToString("O", CultureInfo.InvariantCulture),
            PeriodEnd = period.End.ToString("O", CultureInfo.InvariantCulture)
        };

        try
        {
            var reply = await client.GetRenewalQuoteAsync(
                request,
                deadline: DateTime.UtcNow.Add(Deadline),
                cancellationToken: ct
            );
            return new RenewalQuote(ParseDecimal(reply.Net), ParseDecimal(reply.Tax), ParseDecimal(reply.Total));
        }
        catch (RpcException ex) when (IsUnavailable(ex))
        {
            throw BillingProblems.QuoteUnavailable();
        }
        catch (RpcException ex) when (IsCallerGone(ex, ct))
        {
            throw new OperationCanceledException(ct);
        }
    }

    public async Task<RenewalStatus> GetRenewalAsync(
        Guid invoiceId,
        CancellationToken ct
    )
    {
        try
        {
            var reply = await client.GetRenewalAsync(
                new GetRenewalRequest { InvoiceId = invoiceId.ToString() },
                deadline: DateTime.UtcNow.Add(Deadline),
                cancellationToken: ct
            );
            return new RenewalStatus(
                invoiceId,
                Guid.TryParse(reply.PaymentId, out var paymentId) ? paymentId : null,
                reply.State,
                reply.FailureReason.Length > 0 ? reply.FailureReason : null, // proto3 strings are never null
                DateTime.TryParse(
                    reply.UpdatedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var updatedAt
                )
                    ? updatedAt
                    : null
            );
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw BillingProblems.RenewalNotFound(invoiceId);
        }
        catch (RpcException ex) when (IsUnavailable(ex))
        {
            throw BillingProblems.Unavailable();
        }
        catch (RpcException ex) when (IsCallerGone(ex, ct))
        {
            throw new OperationCanceledException(ct);
        }
    }

    private static decimal ParseDecimal(
        string value
    )
    {
        return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    private static bool IsUnavailable(
        RpcException ex
    )
    {
        return ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded;
    }

    // The HTTP client disconnected; the exception handler writes nothing for an OperationCanceledException.
    private static bool IsCallerGone(
        RpcException ex,
        CancellationToken ct
    )
    {
        return ex.StatusCode == StatusCode.Cancelled && ct.IsCancellationRequested;
    }
}
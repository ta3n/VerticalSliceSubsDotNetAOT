using System.Diagnostics;
using SubscriptionManagement.Infrastructure.Billing;

namespace SubscriptionManagement.Features.Invoices.GetInvoiceRenewal;

public sealed record GetInvoiceRenewalQuery(
    Guid InvoiceId
);

/// <summary>Progress of the saga that collects a renewal invoice, e.g. state AwaitingPayment, Completed or Declined.</summary>
public sealed record RenewalStatusResponse(
    Guid InvoiceId,
    Guid? PaymentId,
    string State,
    string? FailureReason,
    DateTime? UpdatedAt
)
{
    public static RenewalStatusResponse From(
        RenewalStatus r
    )
    {
        return new RenewalStatusResponse(r.InvoiceId, r.PaymentId, r.State, r.FailureReason, r.UpdatedAt);
    }
}

public static class GetInvoiceRenewalHandler
{
    /// <summary>Billing owns the saga; a 404 until it has consumed the invoice's InvoiceIssued message.</summary>
    public static async Task<RenewalStatusResponse> Handle(
        GetInvoiceRenewalQuery query,
        IBillingGateway billing,
        CancellationToken ct
    )
    {
        return RenewalStatusResponse.From(await billing.GetRenewalAsync(query.InvoiceId, ct));
    }
}

public static class GetInvoiceRenewalEndpoint
{
    public static void Map(
        IEndpointRouteBuilder app
    )
    {
        app.MapGet(
            "/invoices/{id}/renewal",
            async (Guid id, IBillingGateway billing, CancellationToken ct) =>
            {
                Activity.Current?.SetTag("invoice.id", id.ToString());
                return TypedResults.Ok(
                    await GetInvoiceRenewalHandler.Handle(new GetInvoiceRenewalQuery(id), billing, ct)
                );
            }
        );
    }
}
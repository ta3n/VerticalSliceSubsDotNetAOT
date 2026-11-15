using Microsoft.AspNetCore.Http.HttpResults;

namespace SubscriptionManagement.Payments.Features.Payments;

public sealed record PaymentResponse(
    Guid Id,
    Guid InvoiceId,
    Guid SubscriptionId,
    decimal Amount,
    string Currency,
    DateOnly PeriodEnd,
    string Status,
    int Version
)
{
    public static PaymentResponse From(
        Payment p
    )
    {
        return new PaymentResponse(
            p.Id,
            p.InvoiceId,
            p.SubscriptionId,
            p.Amount,
            p.Currency,
            p.PeriodEnd,
            p.Status.ToString(),
            p.Version
        );
    }
}

public static class PaymentProblems
{
    /// <summary>RFC 9457 problem with the same <c>type</c> base and slug style as the API's problems.</summary>
    public static ProblemHttpResult NotFound(
        Guid id
    )
    {
        return TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            type: "https://api.demo/errors/payment-not-found",
            title: "Payment not found",
            detail: $"Payment {id} was not found.",
            extensions: new Dictionary<string, object?> { ["paymentId"] = id.ToString() }
        );
    }
}
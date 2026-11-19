using Marten;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SubscriptionManagement.Payments.Features.Payments.GetPayment;

public static class GetPaymentEndpoint
{
  public static void Map(
    IEndpointRouteBuilder app
  )
  {
    app.MapGet(
      "/payments/{id}",
      async Task<Results<Ok<PaymentResponse>, ProblemHttpResult>> (
          Guid id, IQuerySession session, CancellationToken ct
        ) =>
        await session.LoadAsync<Payment>(id, ct) is { } payment
          ? TypedResults.Ok(PaymentResponse.From(payment))
          : PaymentProblems.NotFound(id)
    );
  }
}
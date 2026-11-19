using Npgsql;

namespace SubscriptionManagement.Reporting.Features.Revenue;

public sealed record CurrencyRevenue(
  string Currency,
  int InvoicesIssued,
  decimal Invoiced,
  decimal Collected,
  decimal Refunded
);

public sealed record RevenueSummary(
  IReadOnlyList<CurrencyRevenue> Currencies,
  int PaymentsSucceeded,
  int PaymentsFailed,
  decimal? PaymentSuccessRate,
  int SubscriptionsCanceled
);

public static class GetRevenueSummaryEndpoint
{
  public static void Map(
    IEndpointRouteBuilder app
  )
  {
    app.MapGet(
      "/reporting/revenue",
      async (NpgsqlDataSource db, CancellationToken ct) => TypedResults.Ok(await QueryAsync(db, ct))
    );
  }

  internal static async Task<RevenueSummary> QueryAsync(
    NpgsqlDataSource db,
    CancellationToken ct
  )
  {
    // Money by currency: invoiced = every invoice, collected = invoices whose payment succeeded, refunded = refunds.
    var currencies = new List<CurrencyRevenue>();
    await using (var command = db.CreateCommand(
        """
        WITH inv AS (
            SELECT currency, COUNT(*)::int AS issued, SUM(amount) AS invoiced FROM invoices GROUP BY currency
        ), col AS (
            SELECT i.currency, SUM(i.amount) AS collected
            FROM invoices i
            WHERE EXISTS (SELECT 1 FROM payments p WHERE p.invoice_id = i.invoice_id AND p.outcome = 'succeeded')
            GROUP BY i.currency
        ), ref AS (
            SELECT currency, SUM(amount) AS refunded FROM refunds GROUP BY currency
        )
        SELECT c.currency,
               COALESCE(inv.issued, 0), COALESCE(inv.invoiced, 0),
               COALESCE(col.collected, 0), COALESCE(ref.refunded, 0)
        FROM (SELECT currency FROM inv UNION SELECT currency FROM ref) c
        LEFT JOIN inv USING (currency)
        LEFT JOIN col USING (currency)
        LEFT JOIN ref USING (currency)
        ORDER BY c.currency
        """
      ))
    await using (var reader = await command.ExecuteReaderAsync(ct))
    {
      while (await reader.ReadAsync(ct))
        currencies.Add(
          new CurrencyRevenue(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4)
          )
        );
    }

    await using var counts = db.CreateCommand(
      """
      SELECT (SELECT COUNT(*)::int FROM payments WHERE outcome = 'succeeded'),
             (SELECT COUNT(*)::int FROM payments WHERE outcome = 'failed'),
             (SELECT COUNT(*)::int FROM cancellations)
      """
    );
    await using var row = await counts.ExecuteReaderAsync(ct);
    await row.ReadAsync(ct);
    var succeeded = row.GetInt32(0);
    var failed = row.GetInt32(1);
    decimal? rate = succeeded + failed == 0 ? null : decimal.Round((decimal)succeeded / (succeeded + failed), 4);
    return new RevenueSummary(currencies, succeeded, failed, rate, row.GetInt32(2));
  }
}
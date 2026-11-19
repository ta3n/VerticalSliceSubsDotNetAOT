namespace SubscriptionManagement.Billing.Features.Renewals;

/// <summary>What a renewal costs: the price as net, tax on top. A pure function of its inputs.</summary>
public sealed record RenewalQuote(
  decimal Net,
  decimal Tax,
  decimal Total,
  decimal TaxRate
)
{
  public static RenewalQuote For(
    decimal price,
    decimal taxRate
  )
  {
    // Four decimals, half away from zero: the scale and rounding of the API's invoices.amount NUMERIC(20,4), so
    // the invoice stores exactly the quoted total.
    var net = decimal.Round(price, 4, MidpointRounding.AwayFromZero);
    var tax = decimal.Round(net * taxRate, 4, MidpointRounding.AwayFromZero);
    return new RenewalQuote(net, tax, net + tax, taxRate);
  }
}
using System.Globalization;
using SubscriptionManagement.Billing.Features.Renewals;

namespace SubscriptionManagement.Billing.Tests;

public class RenewalQuoteTests
{
  [Theory]
  [InlineData("10", "0.10", "10", "1.0000", "11.0000")]
  [InlineData("9.99", "0.10", "9.99", "0.9990", "10.9890")]
  [InlineData("19.99", "0.2", "19.99", "3.9980", "23.9880")]
  [InlineData("0", "0.10", "0", "0", "0")]
  [InlineData("25", "0", "25", "0", "25")]
  public void Tax_is_the_rate_applied_to_the_net_price(
    string price,
    string rate,
    string net,
    string tax,
    string total
  )
  {
    var quote = RenewalQuote.For(D(price), D(rate));

    Assert.Equal(D(net), quote.Net);
    Assert.Equal(D(tax), quote.Tax);
    Assert.Equal(D(total), quote.Total);
    Assert.Equal(D(rate), quote.TaxRate);
  }

  [Theory]
  // net rounds to the invoice column scale first, half away from zero
  [InlineData("10.12345", "0.10", "10.1235", "1.0124", "11.1359")]
  [InlineData("10.12344", "0.10", "10.1234", "1.0123", "11.1357")]
  // tax 0.00005 is exactly half a unit of the fourth decimal: it rounds up, not to even
  [InlineData("0.0005", "0.10", "0.0005", "0.0001", "0.0006")]
  [InlineData("0.0004", "0.10", "0.0004", "0.0000", "0.0004")]
  public void Net_and_tax_are_rounded_to_four_decimals_half_away_from_zero(
    string price,
    string rate,
    string net,
    string tax,
    string total
  )
  {
    var quote = RenewalQuote.For(D(price), D(rate));

    Assert.Equal(D(net), quote.Net);
    Assert.Equal(D(tax), quote.Tax);
    Assert.Equal(D(total), quote.Total);
  }

  [Fact]
  public void Total_is_always_net_plus_tax()
  {
    foreach (var price in new[] { 0.01m, 1m, 7.3333m, 49.995m, 123456.789m })
    {
      var quote = RenewalQuote.For(price, 0.0825m);

      Assert.Equal(quote.Net + quote.Tax, quote.Total);
      Assert.Equal(quote.Total, decimal.Round(quote.Total, 4));
    }
  }

  private static decimal D(
    string value
  )
  {
    return decimal.Parse(value, CultureInfo.InvariantCulture);
  }
}
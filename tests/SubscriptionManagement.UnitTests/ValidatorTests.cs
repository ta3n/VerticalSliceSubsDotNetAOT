using SubscriptionManagement.Features.Plans.CreatePlan;
using SubscriptionManagement.Features.Subscriptions.CreateSubscriptions;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.UnitTests;

public class ValidatorTests
{
    private static readonly DateOnly Start = new(2026, 9, 5);

    private static CreateSubscriptionCommand Valid() => new("Netflix", 99000m, Start, null);

    private static IReadOnlyDictionary<string, string[]> Errors(Action act) =>
        Assert.Throws<ApiProblemException>(act).Errors!;

    [Fact]
    public void Valid_subscription_passes_with_and_without_trial()
    {
        CreateSubscriptionValidator.Validate(Valid());
        CreateSubscriptionValidator.Validate(Valid() with { TrialEndsAt = Start.AddDays(7) });
        CreateSubscriptionValidator.Validate(Valid() with { ServiceName = new string('x', 100) });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_service_name_is_rejected(string? name)
    {
        var errors = Errors(() => CreateSubscriptionValidator.Validate(Valid() with { ServiceName = name }));
        Assert.Equal(["must not be blank"], errors["serviceName"]);
    }

    [Fact]
    public void Service_name_over_100_chars_is_rejected()
    {
        var errors = Errors(() => CreateSubscriptionValidator.Validate(Valid() with { ServiceName = new string('x', 101) }));
        Assert.Contains("serviceName", errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_price_is_rejected(int price)
    {
        var errors = Errors(() => CreateSubscriptionValidator.Validate(Valid() with { Price = price }));
        Assert.Equal(["must be greater than 0"], errors["price"]);
    }

    [Fact]
    public void Missing_price_and_start_date_are_rejected()
    {
        var errors = Errors(() => CreateSubscriptionValidator.Validate(new("Netflix", null, null, null)));
        Assert.Equal(["must not be null"], errors["price"]);
        Assert.Equal(["must not be null"], errors["startDate"]);
    }

    [Theory]
    [InlineData(0)]   // equal to start
    [InlineData(-1)]  // before start
    public void Trial_must_end_after_start(int offsetDays)
    {
        var errors = Errors(() => CreateSubscriptionValidator.Validate(Valid() with { TrialEndsAt = Start.AddDays(offsetDays) }));
        Assert.Equal(["must be after startDate"], errors["trialEndsAt"]);
    }

    [Fact]
    public void Price_must_stay_below_what_the_database_can_round_trip()
    {
        CreateSubscriptionValidator.Validate(Valid() with { Price = 9_999_999_999_999_999_999_999_999_999m });
        var errors = Errors(() => CreateSubscriptionValidator.Validate(Valid() with { Price = 10_000_000_000_000_000_000_000_000_000m }));
        Assert.Contains("price", errors.Keys);
    }

    [Fact]
    public void Smallest_positive_price_is_accepted()
    {
        CreateSubscriptionValidator.Validate(Valid() with { Price = 0.0000000000000000000000000001m });
    }

    private static CreatePlanCommand ValidPlan() => new("pro", "Pro", 12m, "USD", "MONTH", 14);

    [Fact]
    public void Valid_plan_passes_and_returns_the_interval()
    {
        Assert.Equal(Features.Plans.PlanInterval.Month, CreatePlanValidator.Validate(ValidPlan()));
        Assert.Equal(Features.Plans.PlanInterval.Year, CreatePlanValidator.Validate(ValidPlan() with { Interval = "YEAR", Amount = 0, TrialDays = 365 }));
    }

    [Theory]
    [InlineData("code", null)]
    [InlineData("name", "")]
    public void Plan_blank_fields_are_rejected(string field, string? value)
    {
        var command = field == "code" ? ValidPlan() with { Code = value } : ValidPlan() with { Name = value };
        Assert.Contains(field, Errors(() => CreatePlanValidator.Validate(command)).Keys);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("interval")]
    [InlineData("trialDays")]
    public void Plan_invalid_values_are_reported_by_field(string field)
    {
        var command = field switch
        {
            "amount" => ValidPlan() with { Amount = -1 },
            "currency" => ValidPlan() with { Currency = "US" },
            "interval" => ValidPlan() with { Interval = "month" }, // case-sensitive
            _ => ValidPlan() with { TrialDays = 366 },
        };
        Assert.Contains(field, Errors(() => CreatePlanValidator.Validate(command)).Keys);
    }
}

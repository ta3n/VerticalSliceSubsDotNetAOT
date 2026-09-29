using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Plans;

public enum PlanInterval
{
    Month,
    Year
}

public static class PlanIntervalNames
{
    public static string ToDb(this PlanInterval interval) => interval == PlanInterval.Month ? "MONTH" : "YEAR";

    public static bool TryParse(string? value, out PlanInterval interval)
    {
        switch (value)
        {
            case "MONTH": interval = PlanInterval.Month; return true;
            case "YEAR": interval = PlanInterval.Year; return true;
            default: interval = default; return false;
        }
    }
}

public static class PlanProblems
{
    public static ApiProblemException NotFound(string code) => new(
        StatusCodes.Status404NotFound, "plan-not-found", "Plan not found",
        $"Plan {code} does not exist or is inactive.",
        new Dictionary<string, object> { ["planCode"] = code });

    public static ApiProblemException Duplicate(string code) => new(
        StatusCodes.Status409Conflict, "plan-already-exists", "Plan already exists",
        $"A plan with code {code} already exists.",
        new Dictionary<string, object> { ["planCode"] = code });
}

/// <summary>Billing-plan catalog entry.</summary>
public sealed record Plan(
    string Code,
    string Name,
    decimal Amount,
    string Currency,
    PlanInterval Interval,
    int TrialDays,
    bool Active);

public sealed record PlanResponse(
    string Code,
    string Name,
    decimal Amount,
    string Currency,
    string Interval,
    int TrialDays,
    bool Active)
{
    public static PlanResponse From(Plan p) => new(p.Code, p.Name, p.Amount, p.Currency, p.Interval.ToDb(), p.TrialDays, p.Active);
}

public sealed record PlanList(IReadOnlyList<PlanResponse> Items);

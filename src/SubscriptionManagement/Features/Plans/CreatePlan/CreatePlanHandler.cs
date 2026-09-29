using Npgsql;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Plans.CreatePlan;

public sealed record CreatePlanCommand(
    string? Code,
    string? Name,
    decimal? Amount,
    string? Currency,
    string? Interval,
    int TrialDays);

public static class CreatePlanValidator
{
    public static PlanInterval Validate(CreatePlanCommand command)
    {
        var errors = new ValidationErrors();
        errors.NotBlank("code", command.Code, 64);
        errors.NotBlank("name", command.Name, 200);

        if (command.Amount is null)
        {
            errors.Add("amount", "must not be null");
        }
        else if (command.Amount < 0)
        {
            errors.Add("amount", "must be greater than or equal to 0");
        }

        if (string.IsNullOrWhiteSpace(command.Currency))
        {
            errors.Add("currency", "must not be blank");
        }
        else if (command.Currency.Length != 3)
        {
            errors.Add("currency", "size must be between 3 and 3");
        }

        var interval = default(PlanInterval);
        if (string.IsNullOrWhiteSpace(command.Interval))
        {
            errors.Add("interval", "must not be blank");
        }
        else if (!PlanIntervalNames.TryParse(command.Interval, out interval))
        {
            errors.Add("interval", "must be MONTH or YEAR");
        }

        if (command.TrialDays < 0)
        {
            errors.Add("trialDays", "must be greater than or equal to 0");
        }
        else if (command.TrialDays > 365)
        {
            errors.Add("trialDays", "must be less than or equal to 365");
        }

        errors.ThrowIfAny();
        return interval;
    }
}

public static class CreatePlanHandler
{
    public static async Task<PlanResponse> Handle(CreatePlanCommand command, NpgsqlDataSource db, CancellationToken ct)
    {
        var interval = CreatePlanValidator.Validate(command);
        var plan = new Plan(command.Code!, command.Name!, command.Amount!.Value, command.Currency!, interval, command.TrialDays, Active: true);

        // ON CONFLICT DO NOTHING makes the duplicate check atomic (no check-then-insert race).
        var inserted = await db.WithConnectionAsync((connection, token) => PlanStore.TryInsertAsync(connection, plan, token), ct);
        return inserted ? PlanResponse.From(plan) : throw PlanProblems.Duplicate(plan.Code);
    }
}

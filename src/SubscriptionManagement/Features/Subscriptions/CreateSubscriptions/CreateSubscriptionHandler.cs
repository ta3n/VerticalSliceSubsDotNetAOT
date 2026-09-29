using Npgsql;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.CreateSubscriptions;

/// <summary>Request body. Nullable members let validation report "missing" instead of binding defaults.</summary>
public sealed record CreateSubscriptionCommand(
    string? ServiceName,
    decimal? Price,
    DateOnly? StartDate,
    DateOnly? TrialEndsAt);

public static class CreateSubscriptionValidator
{
    private const decimal MaxPrice = 10_000_000_000_000_000_000_000_000_000m;

    public static void Validate(CreateSubscriptionCommand command)
    {
        var errors = new ValidationErrors();
        errors.NotBlank("serviceName", command.ServiceName, 100);

        if (command.Price is null)
        {
            errors.Add("price", "must not be null");
        }
        else if (command.Price <= 0)
        {
            errors.Add("price", "must be greater than 0");
        }
        else if (command.Price >= MaxPrice)
        {
            // The NUMERIC(57,28) column can hold larger values than Npgsql can read back into a decimal,
            // which would poison later reads/searches. Everything below 10^28 round-trips.
            errors.Add("price", "must be less than 10000000000000000000000000000");
        }

        if (command.StartDate is null)
        {
            errors.Add("startDate", "must not be null");
        }
        else if (command.TrialEndsAt is { } trial && trial <= command.StartDate)
        {
            errors.Add("trialEndsAt", "must be after startDate");
        }

        errors.ThrowIfAny();
    }
}

public static class CreateSubscriptionHandler
{
    public static async Task<SubscriptionResponse> Handle(
        CreateSubscriptionCommand command,
        NpgsqlDataSource db,
        SubscriptionCache cache,
        CancellationToken ct)
    {
        CreateSubscriptionValidator.Validate(command);

        var subscription = Subscription.Create(
            command.ServiceName!, command.Price!.Value, command.StartDate!.Value, command.TrialEndsAt);

        await db.WithConnectionAsync(async (connection, token) =>
        {
            await SubscriptionStore.InsertAsync(connection, subscription, token);
            return true;
        }, ct);

        var response = SubscriptionResponse.From(subscription);
        await cache.PutAsync(response, CancellationToken.None); // warm the cache after commit
        return response;
    }
}

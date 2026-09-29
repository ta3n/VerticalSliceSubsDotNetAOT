using Npgsql;

namespace SubscriptionManagement.Features.Plans;

public static class PlanStore
{
    private const string Columns = "code, name, amount, currency, billing_interval, trial_days, active";

    /// <summary>Inserts the plan; returns false when the code already exists (unique violation).</summary>
    public static async Task<bool> TryInsertAsync(NpgsqlConnection connection, Plan p, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"INSERT INTO plans ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7) ON CONFLICT (code) DO NOTHING", connection);
        command.Parameters.AddWithValue(p.Code);
        command.Parameters.AddWithValue(p.Name);
        command.Parameters.AddWithValue(p.Amount);
        command.Parameters.AddWithValue(p.Currency);
        command.Parameters.AddWithValue(p.Interval.ToDb());
        command.Parameters.AddWithValue(p.TrialDays);
        command.Parameters.AddWithValue(p.Active);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>Only an active plan is a valid catalog entry.</summary>
    public static async Task<Plan?> FindActiveAsync(NpgsqlConnection connection, string code, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"SELECT {Columns} FROM plans WHERE code = $1 AND active", connection);
        command.Parameters.AddWithValue(code);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public static async Task<List<Plan>> ListActiveAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"SELECT {Columns} FROM plans WHERE active ORDER BY code", connection);
        var rows = new List<Plan>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private static Plan Map(NpgsqlDataReader r) => new(
        Code: r.GetString(0),
        Name: r.GetString(1),
        Amount: r.GetDecimal(2),
        Currency: r.GetString(3),
        Interval: PlanIntervalNames.TryParse(r.GetString(4), out var interval)
            ? interval
            : throw new InvalidOperationException("Unknown plan interval in database."),
        TrialDays: r.GetInt32(5),
        Active: r.GetBoolean(6));
}

using Npgsql;
using NpgsqlTypes;

namespace SubscriptionManagement.Features.Subscriptions;

/// <summary>All SQL for the <c>subscriptions</c> table. Explicit column lists; every value is a bound parameter.</summary>
public static class SubscriptionStore
{
    private const string Columns =
        "id, service_name, price, start_date, trial_ends_at, status, current_period_start, current_period_end";

    public static async Task InsertAsync(NpgsqlConnection connection, Subscription s, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            INSERT INTO subscriptions ({Columns})
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """, connection);
        command.Parameters.AddWithValue(s.Id);
        command.Parameters.AddWithValue(s.ServiceName);
        command.Parameters.AddWithValue(s.Price);
        command.Parameters.AddWithValue(s.StartDate);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Date, Value = (object?)s.TrialEndsAt ?? DBNull.Value });
        command.Parameters.AddWithValue(s.Status.ToDb());
        command.Parameters.AddWithValue(s.CurrentPeriodStart);
        command.Parameters.AddWithValue(s.CurrentPeriodEnd);
        await command.ExecuteNonQueryAsync(ct);
    }

    public static Task<Subscription?> FindAsync(NpgsqlConnection connection, Guid id, CancellationToken ct) =>
        FindCoreAsync(connection, id, forUpdate: false, ct);

    /// <summary>Row-locks the subscription (<c>FOR UPDATE</c>) so concurrent state changes serialise. Requires a transaction.</summary>
    public static Task<Subscription?> FindForUpdateAsync(NpgsqlConnection connection, Guid id, CancellationToken ct) =>
        FindCoreAsync(connection, id, forUpdate: true, ct);

    public static async Task<bool> ExistsAsync(NpgsqlConnection connection, Guid id, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM subscriptions WHERE id = $1)", connection);
        command.Parameters.AddWithValue(id);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    public static async Task SaveStateAsync(NpgsqlConnection connection, Subscription s, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE subscriptions
            SET status = $2, current_period_start = $3, current_period_end = $4
            WHERE id = $1
            """, connection);
        command.Parameters.AddWithValue(s.Id);
        command.Parameters.AddWithValue(s.Status.ToDb());
        command.Parameters.AddWithValue(s.CurrentPeriodStart);
        command.Parameters.AddWithValue(s.CurrentPeriodEnd);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Keyset page ordered by (start_date, id) descending. Fetch <c>limit</c> rows (callers ask for one extra).</summary>
    public static async Task<List<Subscription>> SearchAsync(
        NpgsqlConnection connection,
        SubscriptionStatus? status,
        (DateOnly StartDate, Guid Id)? after,
        int limit,
        CancellationToken ct)
    {
        // The optional filters are fixed SQL fragments chosen in code; values are always parameters.
        var where = new List<string>();
        var parameters = new List<NpgsqlParameter>();
        if (status is { } s)
        {
            parameters.Add(new NpgsqlParameter<string>("status", s.ToDb()));
            where.Add("status = @status");
        }

        if (after is { } cursor)
        {
            parameters.Add(new NpgsqlParameter<DateOnly>("cursorDate", cursor.StartDate));
            parameters.Add(new NpgsqlParameter<Guid>("cursorId", cursor.Id));
            where.Add("(start_date < @cursorDate OR (start_date = @cursorDate AND id < @cursorId))");
        }

        var sql = $"SELECT {Columns} FROM subscriptions"
                  + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
                  + " ORDER BY start_date DESC, id DESC LIMIT @limit";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());
        command.Parameters.Add(new NpgsqlParameter<int>("limit", limit));

        var rows = new List<Subscription>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private static async Task<Subscription?> FindCoreAsync(NpgsqlConnection connection, Guid id, bool forUpdate, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {Columns} FROM subscriptions WHERE id = $1" + (forUpdate ? " FOR UPDATE" : ""), connection);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    private static Subscription Map(NpgsqlDataReader r) => Subscription.Rehydrate(
        id: r.GetGuid(0),
        serviceName: r.GetString(1),
        price: r.GetDecimal(2),
        startDate: r.GetFieldValue<DateOnly>(3),
        trialEndsAt: r.IsDBNull(4) ? null : r.GetFieldValue<DateOnly>(4),
        status: SubscriptionStatusNames.FromDb(r.GetString(5)),
        currentPeriodStart: r.GetFieldValue<DateOnly>(6),
        currentPeriodEnd: r.GetFieldValue<DateOnly>(7));
}

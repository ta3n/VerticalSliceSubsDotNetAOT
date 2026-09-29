using Npgsql;

namespace SubscriptionManagement.Infrastructure;

public static class Db
{
    /// <summary>Runs <paramref name="work"/> in one transaction; any exception rolls it back.</summary>
    public static async Task<T> InTransactionAsync<T>(
        this NpgsqlDataSource dataSource,
        Func<NpgsqlConnection, CancellationToken, Task<T>> work,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var result = await work(connection, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public static async Task<T> WithConnectionAsync<T>(
        this NpgsqlDataSource dataSource,
        Func<NpgsqlConnection, CancellationToken, Task<T>> work,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        return await work(connection, ct);
    }

    /// <summary>Instant → value stored in a UTC <c>timestamp without time zone</c> column.</summary>
    public static DateTime ToStoredUtc(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

    public static DateTime FromStoredUtc(DateTime stored) => DateTime.SpecifyKind(stored, DateTimeKind.Utc);
}

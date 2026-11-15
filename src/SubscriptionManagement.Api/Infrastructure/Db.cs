using Npgsql;

namespace SubscriptionManagement.Infrastructure;

public static class Db
{
    extension(
        NpgsqlDataSource dataSource
    )
    {
        /// <summary>Runs <paramref name="work"/> in one transaction; any exception rolls it back.</summary>
        public async Task<T> InTransactionAsync<T>(
            Func<NpgsqlConnection, CancellationToken, Task<T>> work,
            CancellationToken ct
        )
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            var result = await work(connection, ct);
            await transaction.CommitAsync(ct);
            return result;
        }

        public async Task<T> WithConnectionAsync<T>(
            Func<NpgsqlConnection, CancellationToken, Task<T>> work,
            CancellationToken ct
        )
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            return await work(connection, ct);
        }
    }

    /// <summary>Instant → value stored in a UTC <c>timestamp without time zone</c> column.</summary>
    public static DateTime ToStoredUtc(
        DateTime utc
    )
    {
        return DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
    }

    public static DateTime FromStoredUtc(
        DateTime stored
    )
    {
        return DateTime.SpecifyKind(stored, DateTimeKind.Utc);
    }
}
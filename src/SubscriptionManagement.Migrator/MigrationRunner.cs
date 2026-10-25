using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SubscriptionManagement.Migrator;

/// <summary>
/// Applies the embedded <c>V{n}__{description}.sql</c> files in version order. Applied versions and
/// their checksums are recorded in <c>schema_migrations</c>; a changed checksum is a hard failure.
/// A session-level advisory lock serialises concurrent runners.
/// </summary>
public static partial class MigrationRunner
{
    private const long AdvisoryLockKey = 727_274_001;

    [GeneratedRegex(@"(?:^|\.)V(?<version>\d+)__(?<description>[^.]+)\.sql$")]
    private static partial Regex FileNamePattern();

    public sealed record Migration(int Version, string Description, string Sql, string Checksum);

    public static IReadOnlyList<Migration> LoadEmbedded()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var migrations = new List<Migration>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var match = FileNamePattern().Match(name);
            if (!match.Success)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var sql = reader.ReadToEnd();
            migrations.Add(new Migration(
                int.Parse(match.Groups["version"].Value),
                match.Groups["description"].Value,
                sql,
                Checksum(sql)));
        }

        return migrations.OrderBy(m => m.Version).ToList();
    }

    /// <summary>Returns the number of migrations applied by this run.</summary>
    public static async Task<int> RunAsync(string connectionString, ILogger logger, CancellationToken ct = default)
    {
        var migrations = LoadEmbedded();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        await ExecuteAsync(connection, $"SELECT pg_advisory_lock({AdvisoryLockKey})", ct);
        try
        {
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version     INTEGER      NOT NULL PRIMARY KEY,
                    description VARCHAR(200) NOT NULL,
                    checksum    CHAR(64)     NOT NULL,
                    applied_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
                )
                """, ct);

            var applied = await LoadAppliedAsync(connection, ct);
            var count = 0;
            foreach (var migration in migrations)
            {
                if (applied.TryGetValue(migration.Version, out var recorded))
                {
                    if (!string.Equals(recorded, migration.Checksum, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Migration V{migration.Version} ({migration.Description}) was modified after it was applied.");
                    }

                    continue;
                }

                await using var tx = await connection.BeginTransactionAsync(ct);
                await ExecuteAsync(connection, migration.Sql, ct);
                await using (var insert = new NpgsqlCommand(
                    "INSERT INTO schema_migrations (version, description, checksum) VALUES ($1, $2, $3)", connection))
                {
                    insert.Parameters.AddWithValue(migration.Version);
                    insert.Parameters.AddWithValue(migration.Description);
                    insert.Parameters.AddWithValue(migration.Checksum);
                    await insert.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
                logger.LogInformation("Applied migration V{Version} {Description}", migration.Version, migration.Description);
                count++;
            }

            logger.LogInformation("Schema is up to date ({Applied} applied, {Total} total)", count, migrations.Count);
            return count;
        }
        finally
        {
            await ExecuteAsync(connection, $"SELECT pg_advisory_unlock({AdvisoryLockKey})", CancellationToken.None);
        }
    }

    private static async Task<Dictionary<int, string>> LoadAppliedAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var applied = new Dictionary<int, string>();
        await using var command = new NpgsqlCommand("SELECT version, checksum FROM schema_migrations", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            applied[reader.GetInt32(0)] = reader.GetString(1);
        }

        return applied;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Checksum(string sql)
    {
        var normalized = sql.Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}

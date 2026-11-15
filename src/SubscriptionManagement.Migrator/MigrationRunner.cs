using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DbUp;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SubscriptionManagement.Migrator;

/// <summary>
/// Applies the embedded <c>V{n}__{description}.sql</c> files in version order with DbUp. Applied versions and
/// their checksums are recorded in <c>schema_migrations</c> (<see cref="SchemaMigrationsJournal"/>); a changed
/// checksum is a hard failure. A session-level advisory lock serialises concurrent runners.
/// </summary>
public static partial class MigrationRunner
{
    private const long AdvisoryLockKey = 727_274_001;

    [GeneratedRegex(@"(?:^|\.)V(?<version>\d+)__(?<description>[^.]+)\.sql$")]
    private static partial Regex FileNamePattern();

    public sealed record Migration(
        int Version,
        string Description,
        string Sql,
        string Checksum
    );

    public static IReadOnlyList<Migration> LoadEmbedded()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var migrations = new List<Migration>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var match = FileNamePattern().Match(name);
            if (!match.Success) continue;

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var sql = reader.ReadToEnd();
            migrations.Add(
                new Migration(
                    int.Parse(match.Groups["version"].Value),
                    match.Groups["description"].Value,
                    sql,
                    Checksum(sql)
                )
            );
        }

        return migrations.OrderBy(m => m.Version).ToList();
    }

    /// <summary>Returns the number of migrations applied by this run.</summary>
    public static async Task<int> RunAsync(
        string connectionString,
        ILogger logger,
        CancellationToken ct = default
    )
    {
        var migrations = LoadEmbedded();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var lockConnection = await dataSource.OpenConnectionAsync(ct);

        // DbUp has no lock of its own: this session holds it while DbUp works on connections of its own.
        await ExecuteAsync(lockConnection, $"SELECT pg_advisory_lock({AdvisoryLockKey})", ct);
        try
        {
            var engine = DeployChanges.To.PostgresqlDatabase(connectionString)
                .WithScripts(new EmbeddedScripts(migrations))
                .WithScriptNameComparer(new VersionOrder())
                .JournalTo(new SchemaMigrationsJournal(connectionString, migrations))
                // The journal row is written in the script's own transaction, as it always was.
                .WithTransactionPerScript()
                // The scripts are plain SQL: no $variable$ substitution over them.
                .WithVariablesDisabled()
                .LogToNowhere()
                .Build();

            var result = await Task.Run(engine.PerformUpgrade, ct);
            if (!result.Successful) ExceptionDispatchInfo.Capture(result.Error).Throw();

            foreach (var script in result.Scripts)
            {
                var migration = migrations.Single(m => ScriptName(m) == script.Name);
                LogApplied(logger, migration.Version, migration.Description);
            }

            var count = result.Scripts.Count();
            LogUpToDate(logger, count, migrations.Count);
            return count;
        }
        finally
        {
            await ExecuteAsync(lockConnection, $"SELECT pg_advisory_unlock({AdvisoryLockKey})", CancellationToken.None);
        }
    }

    /// <summary>The name a migration has inside DbUp and its journal.</summary>
    internal static string ScriptName(
        Migration migration
    )
    {
        return $"V{migration.Version}__{migration.Description}";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied migration V{Version} {Description}")]
    private static partial void LogApplied(
        ILogger logger,
        int version,
        string description
    );

    [LoggerMessage(Level = LogLevel.Information, Message = "Schema is up to date ({Applied} applied, {Total} total)")]
    private static partial void LogUpToDate(
        ILogger logger,
        int applied,
        int total
    );

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken ct
    )
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal static string Checksum(
        string sql
    )
    {
        var normalized = sql.Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
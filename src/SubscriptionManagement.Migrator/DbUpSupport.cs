using System.Data;
using DbUp.Engine;
using DbUp.Engine.Transactions;
using Npgsql;

namespace SubscriptionManagement.Migrator;

/// <summary>The embedded migrations as DbUp scripts, named <c>V{n}__{description}</c>.</summary>
internal sealed class EmbeddedScripts(
    IReadOnlyList<MigrationRunner.Migration> migrations
) : IScriptProvider
{
    public IEnumerable<SqlScript> GetScripts(
        IConnectionManager connectionManager
    )
    {
        return migrations.Select(m => new SqlScript(MigrationRunner.ScriptName(m), m.Sql));
    }
}

/// <summary>DbUp sorts names as text, which would put V10 before V2.</summary>
internal sealed class VersionOrder : IComparer<string>
{
    public int Compare(
        string? x,
        string? y
    )
    {
        return VersionOf(x).CompareTo(VersionOf(y));
    }

    private static int VersionOf(
        string? name
    )
    {
        var end = name?.IndexOf("__", StringComparison.Ordinal) ?? -1;
        return end > 1 && int.TryParse(name![1..end], out var version) ? version : int.MaxValue;
    }
}

/// <summary>
/// DbUp's journal over the existing <c>schema_migrations</c> table (version, description, checksum, applied_at), so
/// databases created before DbUp keep their history. Unlike DbUp's own journal it records a checksum and refuses to
/// continue when an applied migration was edited.
/// </summary>
internal sealed class SchemaMigrationsJournal(
    string connectionString,
    IReadOnlyList<MigrationRunner.Migration> known
) : IJournal
{
    /// <summary>Names of the known migrations that were applied (matched by version, so a renamed file is not run again).</summary>
    public string[] GetExecutedScripts()
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using (var exists = new NpgsqlCommand("SELECT to_regclass('schema_migrations') IS NOT NULL", connection))
        {
            if (!(bool)exists.ExecuteScalar()!) return [];
        }

        using var command = new NpgsqlCommand("SELECT version, description, checksum FROM schema_migrations", connection);
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            var version = reader.GetInt32(0);
            var migration = known.FirstOrDefault(m => m.Version == version);
            if (migration is null) continue;

            if (!string.Equals(reader.GetString(2), migration.Checksum, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Migration V{version} ({reader.GetString(1)}) was modified after it was applied."
                );

            names.Add(MigrationRunner.ScriptName(migration));
        }

        return names.ToArray();
    }

    public void StoreExecutedScript(
        SqlScript script,
        Func<IDbCommand> dbCommandFactory
    )
    {
        EnsureTableExistsAndIsLatestVersion(dbCommandFactory);
        var migration = known.Single(m => MigrationRunner.ScriptName(m) == script.Name);
        using var command = dbCommandFactory();
        command.CommandText =
            "INSERT INTO schema_migrations (version, description, checksum) VALUES (@version, @description, @checksum)";
        Add(command, "version", migration.Version);
        Add(command, "description", migration.Description);
        Add(command, "checksum", MigrationRunner.Checksum(script.Contents));
        command.ExecuteNonQuery();
    }

    public void EnsureTableExistsAndIsLatestVersion(
        Func<IDbCommand> dbCommandFactory
    )
    {
        using var command = dbCommandFactory();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version     INTEGER      NOT NULL PRIMARY KEY,
                description VARCHAR(200) NOT NULL,
                checksum    CHAR(64)     NOT NULL,
                applied_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
            )
            """;
        command.ExecuteNonQuery();
    }

    private static void Add(
        IDbCommand command,
        string name,
        object value
    )
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

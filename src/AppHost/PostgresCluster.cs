namespace AppHost;

/// <summary>
/// PostgreSQL high availability and pooling around the Aspire primary: streaming replicas (hot standbys cloned from the
/// primary) and PgBouncer in front of the primary for the API. Together with the primary this is the "3 pods" topology.
/// <para>
/// Only the API goes through PgBouncer (transaction pooling). The migrator (session-level advisory lock) and the
/// Billing / Payments / Relay services (Wolverine and Marten take session-level advisory locks) keep a direct
/// connection to the primary: those features do not survive a pooler that hands the server connection back after every
/// transaction.
/// </para>
/// </summary>
internal static class PostgresCluster
{
  private const string ConfigDir = "postgres";

  /// <summary>
  /// Settings the primary needs so standbys can stream from it: the replication entry of <c>pg_hba.conf</c> and WAL
  /// kept long enough for a standby that starts a little after its base backup.
  /// </summary>
  public static IResourceBuilder<PostgresServerResource> WithReplication(
    this IResourceBuilder<PostgresServerResource> postgres
  )
  {
    return postgres
      .WithBindMount($"{ConfigDir}/pg_hba.conf", "/etc/postgresql/pg_hba.conf", true)
      .WithArgs("-c", "hba_file=/etc/postgresql/pg_hba.conf", "-c", "wal_keep_size=1GB");
  }

  /// <summary>
  /// <c>Postgres:Standbys</c> read-only replicas. Each clones the primary on its first start (pg_basebackup) and then
  /// streams WAL from it. The data volume is anonymous, so every AppHost run clones afresh and a recreated primary can
  /// never meet a standby of its previous life.
  /// </summary>
  public static IReadOnlyList<IResourceBuilder<ContainerResource>> AddStandbys(
    this IDistributedApplicationBuilder builder,
    IResourceBuilder<PostgresServerResource> postgres,
    int maxConnections
  )
  {
    var count = builder.Configuration.Required<int>("Postgres:Standbys");
    var primary = postgres.Resource.PrimaryEndpoint;
    var standbys = new List<IResourceBuilder<ContainerResource>>();
    for (var i = 1; i <= count; i++)
      standbys.Add(
        builder.AddContainer($"postgres-replica-{i.ToString()}", "postgres", "17")
          .WithBindMount($"{ConfigDir}/replica.sh", "/scripts/replica.sh", true)
          .WithEntrypoint("bash")
          .WithArgs("/scripts/replica.sh")
          .WithEnvironment("PRIMARY_HOST", primary.Property(EndpointProperty.Host))
          .WithEnvironment("PRIMARY_PORT", primary.Property(EndpointProperty.Port))
          .WithEnvironment("PGPASSWORD", postgres.Resource.PasswordParameter)
          // A standby must allow at least as many connections as the primary, or it refuses to start.
          .WithEnvironment("POSTGRES_ARGS", $"-c max_connections={maxConnections.ToString()}")
          .WithEndpoint(targetPort: 5432, name: "tcp")
          .WithParentRelationship(postgres)
          .WaitFor(postgres)
          .WithIconName("DatabasePlugConnected")
      );
    return standbys;
  }

  /// <summary>
  /// PgBouncer in transaction-pooling mode in front of the primary: thousands of client connections share
  /// <c>PgBouncer:PoolSize</c> server connections. Protocol-level prepared statements keep working
  /// (<c>max_prepared_statements</c>).
  /// </summary>
  public static IResourceBuilder<ContainerResource> AddPgBouncer(
    this IDistributedApplicationBuilder builder,
    IResourceBuilder<PostgresServerResource> postgres
  )
  {
    var settings = builder.Configuration.GetSection("PgBouncer");
    var primary = postgres.Resource.PrimaryEndpoint;
    return builder.AddContainer("pgbouncer", "edoburu/pgbouncer", "v1.24.1-p1")
      .WithEnvironment("DB_HOST", primary.Property(EndpointProperty.Host))
      .WithEnvironment("DB_PORT", primary.Property(EndpointProperty.Port))
      .WithEnvironment("DB_USER", "postgres")
      .WithEnvironment("DB_PASSWORD", postgres.Resource.PasswordParameter)
      .WithEnvironment("AUTH_TYPE", "scram-sha-256")
      .WithEnvironment("POOL_MODE", "transaction")
      .WithEnvironment("DEFAULT_POOL_SIZE", settings.Required<string>("PoolSize"))
      .WithEnvironment("MAX_CLIENT_CONN", settings.Required<string>("MaxClientConnections"))
      .WithEnvironment("MAX_PREPARED_STATEMENTS", settings.Required<string>("MaxPreparedStatements"))
      .WithEnvironment("ADMIN_USERS", "postgres")
      .WithEnvironment("STATS_USERS", "postgres")
      .WithEndpoint(targetPort: 5432, name: "tcp")
      .WithParentRelationship(postgres)
      .WaitFor(postgres)
      .WithIconName("ArrowRoutingRectangleMultiple");
  }

  /// <summary>The API's connection string: PgBouncer instead of the database, the pool is the client side of it.</summary>
  public static ReferenceExpression ApiConnectionString(
    IResourceBuilder<ContainerResource> pgbouncer,
    IResourceBuilder<PostgresServerResource> postgres,
    IResourceBuilder<PostgresDatabaseResource> database,
    int poolSize
  )
  {
    var endpoint = pgbouncer.GetEndpoint("tcp");
    // No Reset On Close: Npgsql's DISCARD ALL on every return to its pool would be one more round trip through the
    // pooler for nothing — transaction pooling never lets session state leak.
    return ReferenceExpression.Create(
      $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};"
      + $"Username=postgres;Password={postgres.Resource.PasswordParameter};Database={database.Resource.DatabaseName};"
      + $"Maximum Pool Size={poolSize.ToString()};No Reset On Close=true"
    );
  }
}
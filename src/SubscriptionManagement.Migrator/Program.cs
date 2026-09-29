using Microsoft.Extensions.Logging;
using SubscriptionManagement.Migrator;

// Usage: SubscriptionManagement.Migrator [connection-string]
// The connection string may also come from ConnectionStrings__Postgres.
var connectionString = args.Length > 0
    ? args[0]
    : Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("No connection string. Pass it as the first argument or set ConnectionStrings__Postgres.");
    return 2;
}

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
var logger = loggerFactory.CreateLogger("Migrator");

try
{
    await MigrationRunner.RunAsync(connectionString, logger);
    return 0;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Migration failed");
    return 1;
}

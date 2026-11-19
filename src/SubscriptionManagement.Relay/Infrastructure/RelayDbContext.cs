using Microsoft.EntityFrameworkCore;

namespace SubscriptionManagement.Relay.Infrastructure;

/// <summary>
/// Maps no entities of its own: it exists so the outbox drain can publish through Wolverine's EF Core outbox, in the
/// same transaction as its raw SQL on <c>outbox_messages</c> (a table owned by the migrator).
/// </summary>
public sealed class RelayDbContext(
  DbContextOptions<RelayDbContext> options
) : DbContext(options);
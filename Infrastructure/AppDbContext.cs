using Microsoft.EntityFrameworkCore;
using SubscriptionManagement.Features.Subscriptions;

namespace SubscriptionManagement.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
}
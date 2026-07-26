using Microsoft.EntityFrameworkCore;
using OrderSystem.OrderService.Persistence;

namespace OrderSystem.OrderService.DbMigration;

public sealed class OrderDbMigrator(OrderDbContext dbContext) : IOrderDbMigrator
{
    public Task MigrateAsync(CancellationToken cancellationToken) => dbContext.Database.MigrateAsync(cancellationToken);
}

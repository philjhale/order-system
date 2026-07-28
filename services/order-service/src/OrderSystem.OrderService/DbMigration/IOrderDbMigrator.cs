namespace OrderSystem.OrderService.DbMigration;

public interface IOrderDbMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken);
}

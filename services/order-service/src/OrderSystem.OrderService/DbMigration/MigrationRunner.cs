using Microsoft.Extensions.Logging;

namespace OrderSystem.OrderService.DbMigration;

/// <summary>
/// Entry point for the `dotnet OrderSystem.OrderService.dll migrate` mode run by the
/// azurerm_container_app_job (services/order-service/infra/terraform) against a freshly-provisioned, schema-less database:
/// provision this service's managed identity as a contained DB user first, since EF Core's own
/// migration connection authenticates as that identity and would otherwise be rejected.
/// </summary>
public sealed class MigrationRunner(
    ISqlContainedUserProvisioner userProvisioner,
    IOrderDbMigrator migrator,
    ILogger<MigrationRunner> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Provisioning contained database user");
        await userProvisioner.EnsureContainedUserAsync(cancellationToken);

        logger.LogInformation("Applying EF Core migrations");
        await migrator.MigrateAsync(cancellationToken);
    }
}

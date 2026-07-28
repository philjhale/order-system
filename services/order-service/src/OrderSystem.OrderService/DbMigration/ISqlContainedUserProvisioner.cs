namespace OrderSystem.OrderService.DbMigration;

/// <summary>
/// Creates (idempotently) this service's managed identity as a contained database user, so it
/// can subsequently authenticate to the database as itself. Must run before <see cref="IOrderDbMigrator"/>,
/// since a freshly-provisioned database has no contained users yet and EF Core's own connection
/// (authenticating as the managed identity) would be rejected.
/// </summary>
public interface ISqlContainedUserProvisioner
{
    Task EnsureContainedUserAsync(CancellationToken cancellationToken);
}

using Microsoft.Extensions.Logging.Abstractions;
using OrderSystem.OrderService.DbMigration;

namespace OrderSystem.OrderService.Tests.DbMigration;

public sealed class MigrationRunnerTests
{
    [Fact]
    public async Task RunAsync_provisions_contained_user_before_migrating()
    {
        var calls = new List<string>();
        var provisioner = new RecordingProvisioner(calls);
        var migrator = new RecordingMigrator(calls);
        var runner = new MigrationRunner(provisioner, migrator, NullLogger<MigrationRunner>.Instance);

        await runner.RunAsync(CancellationToken.None);

        Assert.Equal(["provision", "migrate"], calls);
    }

    [Fact]
    public async Task RunAsync_does_not_migrate_if_provisioning_fails()
    {
        var calls = new List<string>();
        var provisioner = new ThrowingProvisioner();
        var migrator = new RecordingMigrator(calls);
        var runner = new MigrationRunner(provisioner, migrator, NullLogger<MigrationRunner>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(CancellationToken.None));

        Assert.Empty(calls);
    }

    private sealed class RecordingProvisioner(List<string> calls) : ISqlContainedUserProvisioner
    {
        public Task EnsureContainedUserAsync(CancellationToken cancellationToken)
        {
            calls.Add("provision");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingProvisioner : ISqlContainedUserProvisioner
    {
        public Task EnsureContainedUserAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("provisioning failed");
    }

    private sealed class RecordingMigrator(List<string> calls) : IOrderDbMigrator
    {
        public Task MigrateAsync(CancellationToken cancellationToken)
        {
            calls.Add("migrate");
            return Task.CompletedTask;
        }
    }
}

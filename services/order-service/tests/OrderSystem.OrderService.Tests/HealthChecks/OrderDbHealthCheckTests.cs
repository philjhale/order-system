using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderSystem.OrderService.HealthChecks;
using OrderSystem.OrderService.Persistence;

namespace OrderSystem.OrderService.Tests.HealthChecks;

public sealed class OrderDbHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenDatabaseReachable_ReturnsHealthy()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase($"order-db-health-check-tests-{Guid.NewGuid()}")
            .Options;
        using var db = new OrderDbContext(options);
        var check = new OrderDbHealthCheck(db);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenDatabaseUnreachable_ReturnsUnhealthy()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase($"order-db-health-check-tests-{Guid.NewGuid()}")
            .Options;
        var db = new OrderDbContext(options);
        db.Dispose();
        var check = new OrderDbHealthCheck(db);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}

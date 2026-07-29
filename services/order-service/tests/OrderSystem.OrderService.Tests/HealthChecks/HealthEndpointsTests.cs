using System.Net;
using OrderSystem.OrderService.Tests.Api;

namespace OrderSystem.OrderService.Tests.HealthChecks;

// Guards the routing split behind the Container App's probes (services/order-service/infra/terraform):
// liveness/readiness must stay DB-free so they can poll continuously without keeping Serverless
// SQL billed around the clock, while startup is the one endpoint allowed to touch the database.
public sealed class HealthEndpointsTests
{
    [Fact]
    public async Task HealthLive_ReturnsHealthy_WithoutTouchingDatabase()
    {
        await using var factory = new OrderApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthStartup_RunsOrderDbHealthCheck_AndReturnsHealthy()
    {
        await using var factory = new OrderApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/startup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

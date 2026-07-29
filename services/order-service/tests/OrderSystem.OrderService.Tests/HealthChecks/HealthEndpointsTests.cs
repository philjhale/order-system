using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderSystem.OrderService.Tests.Api;

namespace OrderSystem.OrderService.Tests.HealthChecks;

// Guards the routing split behind the Container App's probes (services/order-service/infra/terraform):
// liveness/readiness must stay DB-free so they can poll continuously without keeping Serverless
// SQL billed around the clock, while startup is the one endpoint allowed to touch the database.
public sealed class HealthEndpointsTests
{
    [Fact]
    public async Task HealthLive_ReturnsHealthy_WithoutRunningTheDbCheck()
    {
        await using var factory = MakeFactoryWithFakeDbCheck(HealthCheckResult.Unhealthy());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        // The DB check is forced unhealthy above; /health/live still returns 200 only if it
        // truly runs zero checks rather than happening to pass.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthStartup_ReturnsUnhealthy_WhenTheDbCheckFails()
    {
        await using var factory = MakeFactoryWithFakeDbCheck(HealthCheckResult.Unhealthy());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/startup");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task HealthStartup_ReturnsHealthy_WhenTheDbCheckSucceeds()
    {
        await using var factory = MakeFactoryWithFakeDbCheck(HealthCheckResult.Healthy());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/startup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Swaps the "order-db" registration for a fake with a fixed result, so tests can prove
    // /health/startup actually gates on it without depending on OrderDbHealthCheck's real
    // InMemory-provider behavior (CanConnectAsync() is always true against InMemory).
    // WithWebHostBuilder returns a new WebApplicationFactory<Program> that composes this
    // configuration on top of OrderApiFactory's own (its InMemory DB swap etc.), but the
    // returned instance isn't an OrderApiFactory, so callers only get the base type back.
    private static WebApplicationFactory<Program> MakeFactoryWithFakeDbCheck(HealthCheckResult result) =>
        new OrderApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    options.Registrations.Clear();
                    options.Registrations.Add(
                        new HealthCheckRegistration("order-db", new FixedResultHealthCheck(result), failureStatus: null, tags: null));
                })));

    private sealed class FixedResultHealthCheck(HealthCheckResult result) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}

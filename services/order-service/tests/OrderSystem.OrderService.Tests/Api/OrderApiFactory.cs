using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OrderSystem.Contracts.Events;
using OrderSystem.Messaging;
using OrderSystem.OrderService.Persistence;

namespace OrderSystem.OrderService.Tests.Api;

// Swaps the SQL Server DbContext for a fresh per-instance InMemory database and the
// real event publisher for a capturing spy, so tests exercise the actual HTTP
// pipeline/DI wiring without needing a live SQL Server or Service Bus.
public sealed class OrderApiFactory : WebApplicationFactory<Program>
{
    public RecordingEventPublisher Publisher { get; } = new();

    // Generated once per factory instance: ConfigureWebHost's callback can run more
    // than once internally (WebApplicationFactory builds a throwaway host in
    // addition to the one that actually serves requests), so a Guid generated inside
    // the lambda would produce two different InMemory database names — one backing
    // `factory.Services`, a different one backing the real server.
    private readonly string _databaseName = $"order-service-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // AddOrderDbContext's UseSqlServer call registers SQL Server provider
            // services directly into the app's IServiceCollection (not a private
            // provider), so swapping in InMemory requires stripping every
            // EF Core service the SqlServer registration added, not just the
            // DbContextOptions<OrderDbContext> descriptor — otherwise EF sees two
            // providers registered and refuses to pick one.
            services.RemoveAll<DbContextOptions<OrderDbContext>>();
            var efCoreDescriptors = services
                .Where(d => d.ServiceType.Assembly.GetName().Name?.StartsWith("Microsoft.EntityFrameworkCore") == true)
                .ToList();
            foreach (var descriptor in efCoreDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<OrderDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher>(Publisher);

            // These tests only exercise the HTTP API, not event consumption — running
            // OrderEventConsumerHostedService here serves no purpose and, combined with
            // WebApplicationFactory's internal "throwaway host" (see the comment on
            // _databaseName above), was observed to race its background subscriptions
            // against host teardown and intermittently fail CI with an
            // ObjectDisposedException during test-class cleanup. OrderEventConsumerTests
            // and OrderEventConsumerIntegrationTests cover the consumer itself.
            services.RemoveAll<IHostedService>();
        });
    }
}

public sealed class RecordingEventPublisher : IEventPublisher
{
    private readonly List<object> _published = [];

    public IReadOnlyList<object> Published => _published;

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IOrderScopedEvent
    {
        _published.Add(@event);
        return Task.CompletedTask;
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderSystem.OrderService.Persistence;

namespace OrderSystem.OrderService.Tests.Api;

// Unlike OrderApiFactory, this leaves the real InMemoryEventBus wired as both
// IEventPublisher and IEventSubscriber (task 9's registration in
// OrderService.Messaging.ServiceCollectionExtensions) so tests can publish an event
// through the same bus OrderEventConsumerHostedService subscribed to, and observe the
// consumer's effect via the HTTP API.
public sealed class OrderEventConsumerApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"order-service-consumer-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
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
        });
    }
}

using OrderSystem.Messaging;

namespace OrderSystem.OrderService.Messaging;

public static class ServiceCollectionExtensions
{
    // ServiceBus:FullyQualifiedNamespace is only populated once the Container App's
    // managed identity has Service Bus RBAC (task 10); until then — local `dotnet run`
    // and this service's own tests — publishing falls back to the in-process bus.
    public static IServiceCollection AddEventPublisher(this IServiceCollection services, string? serviceBusNamespace)
    {
        if (string.IsNullOrWhiteSpace(serviceBusNamespace))
        {
            services.AddSingleton<InMemoryEventBus>();
            services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InMemoryEventBus>());
        }
        else
        {
            services.AddSingleton(new ServiceBusEventBusOptions { FullyQualifiedNamespace = serviceBusNamespace });
            services.AddSingleton<ServiceBusEventBus>();
            services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<ServiceBusEventBus>());
        }

        return services;
    }
}

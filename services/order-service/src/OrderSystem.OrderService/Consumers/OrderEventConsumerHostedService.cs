using OrderSystem.Contracts.Events;
using OrderSystem.Messaging;

namespace OrderSystem.OrderService.Consumers;

/// <summary>
/// Subscribes Order Service to every event it consumes (task 9) for the lifetime of the
/// process. Each delivered message is handled in its own DI scope, so the scoped
/// OrderDbContext resolved for OrderEventConsumer is never shared across concurrent
/// deliveries from different sessions (OrderIds).
/// </summary>
public sealed class OrderEventConsumerHostedService(
    IEventSubscriber subscriber,
    IServiceScopeFactory scopeFactory) : IHostedService
{
    public const string SubscriptionName = "order-service";

    private readonly List<IAsyncDisposable> _subscriptions = [];

    // WebApplicationFactory has been observed to invoke a hosted service's StopAsync more
    // than once during teardown (its IDisposable and IAsyncDisposable paths both drive the
    // underlying Host's StopAsync) — without this guard, a second concurrent call iterates
    // _subscriptions while the first call's Clear() is mutating it.
    private int _stopped;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Add(await subscriber.SubscribeAsync<InventoryReserved>(
            SubscriptionName,
            (@event, ct) => HandleAsync(consumer => consumer.HandleInventoryReservedAsync(@event, ct)),
            cancellationToken));

        _subscriptions.Add(await subscriber.SubscribeAsync<InventoryFailed>(
            SubscriptionName,
            (@event, ct) => HandleAsync(consumer => consumer.HandleInventoryFailedAsync(@event, ct)),
            cancellationToken));

        _subscriptions.Add(await subscriber.SubscribeAsync<PaymentCompleted>(
            SubscriptionName,
            (@event, ct) => HandleAsync(consumer => consumer.HandlePaymentCompletedAsync(@event, ct)),
            cancellationToken));

        _subscriptions.Add(await subscriber.SubscribeAsync<PaymentFailed>(
            SubscriptionName,
            (@event, ct) => HandleAsync(consumer => consumer.HandlePaymentFailedAsync(@event, ct)),
            cancellationToken));

        _subscriptions.Add(await subscriber.SubscribeAsync<OrderShipped>(
            SubscriptionName,
            (@event, ct) => HandleAsync(consumer => consumer.HandleOrderShippedAsync(@event, ct)),
            cancellationToken));

        _subscriptions.Add(await subscriber.SubscribeAsync<OrderDelivered>(
            SubscriptionName,
            (@event, ct) => HandleAsync(consumer => consumer.HandleOrderDeliveredAsync(@event, ct)),
            cancellationToken));

        _subscriptions.Add(await subscriber.SubscribeAsync<InventoryReleased>(
            SubscriptionName,
            (@event, ct) => HandleAsync(consumer => consumer.HandleInventoryReleasedAsync(@event, ct)),
            cancellationToken));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        foreach (var subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }

        _subscriptions.Clear();
    }

    private async Task<MessageOutcome> HandleAsync(Func<OrderEventConsumer, Task<MessageOutcome>> handle)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetRequiredService<OrderEventConsumer>();
        return await handle(consumer);
    }
}

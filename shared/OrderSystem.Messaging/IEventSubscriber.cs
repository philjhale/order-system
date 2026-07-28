using OrderSystem.Contracts.Events;

namespace OrderSystem.Messaging;

public delegate Task<MessageOutcome> EventMessageHandler<TEvent>(TEvent @event, CancellationToken cancellationToken)
    where TEvent : class, IOrderScopedEvent;

/// <summary>
/// Subscribes a handler to one event type's topic, delivered to the given named
/// subscription. Delivery is ordered per OrderId (Service Bus session), but only
/// within this one subscription — a consumer with subscriptions to several topics has
/// no ordering guarantee across them. A handler that receives an event before its
/// precondition state has been reached should abandon it for redelivery rather than
/// reject/drop it, relying on the subscription's own MaxDeliveryCount for poison messages.
/// </summary>
public interface IEventSubscriber
{
    Task<IAsyncDisposable> SubscribeAsync<TEvent>(
        string subscriptionName,
        EventMessageHandler<TEvent> handler,
        CancellationToken cancellationToken = default)
        where TEvent : class, IOrderScopedEvent;
}

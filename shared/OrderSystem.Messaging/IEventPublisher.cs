using OrderSystem.Contracts.Events;

namespace OrderSystem.Messaging;

public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IOrderScopedEvent;
}

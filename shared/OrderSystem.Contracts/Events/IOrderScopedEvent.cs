namespace OrderSystem.Contracts.Events;

/// <summary>
/// Implemented by every event DTO so messaging infrastructure can route/partition
/// (Service Bus session id) by order without each publisher passing OrderId separately.
/// </summary>
public interface IOrderScopedEvent
{
    Guid OrderId { get; }
}

using OrderSystem.Contracts;

namespace OrderSystem.OrderService.Domain;

// Append-only audit record — one row per Order state transition. Never
// updated or deleted; see docs/SPEC.md Non-Functional Requirements
// "Auditable state".
public sealed class OrderEvent
{
    public Guid EventId { get; private set; }
    public Guid OrderId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public OrderStatus? FromState { get; private set; }
    public OrderStatus ToState { get; private set; }
    public string EventData { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private OrderEvent() { } // EF Core

    internal OrderEvent(
        Guid eventId,
        Guid orderId,
        string eventType,
        OrderStatus? fromState,
        OrderStatus toState,
        string eventData,
        DateTimeOffset createdAt)
    {
        EventId = eventId;
        OrderId = orderId;
        EventType = eventType;
        FromState = fromState;
        ToState = toState;
        EventData = eventData;
        CreatedAt = createdAt;
    }
}

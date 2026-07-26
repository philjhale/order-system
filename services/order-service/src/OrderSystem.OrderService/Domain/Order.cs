using System.Text.Json;
using OrderSystem.Contracts;

namespace OrderSystem.OrderService.Domain;

public sealed class Order
{
    private readonly List<OrderItem> _items = [];
    private readonly List<OrderEvent> _orderEvents = [];

    public Guid OrderId { get; private set; }
    public Guid UserId { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string ShippingAddress { get; private set; } = string.Empty;
    public string PaymentMethod { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;
    public IReadOnlyList<OrderEvent> OrderEvents => _orderEvents;

    private Order() { } // EF Core

    // TotalAmount is taken as submitted by the client with the order
    // request, not derived from Items — there is no catalog/pricing service
    // in scope to validate it against (docs/SPEC.md Functional Requirements
    // "Place order" known limitation).
    public static Order Create(
        Guid orderId,
        Guid userId,
        IReadOnlyList<NewOrderItem> items,
        decimal totalAmount,
        string shippingAddress,
        string paymentMethod,
        DateTimeOffset now)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("An order must have at least one item.", nameof(items));
        }

        var order = new Order
        {
            OrderId = orderId,
            UserId = userId,
            Status = OrderStatus.Created,
            TotalAmount = totalAmount,
            ShippingAddress = shippingAddress,
            PaymentMethod = paymentMethod,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var item in items)
        {
            order._items.Add(new OrderItem(orderId, item.ProductId, item.Quantity, item.UnitPrice));
        }

        order._orderEvents.Add(new OrderEvent(
            Guid.NewGuid(),
            orderId,
            eventType: OrderEventType.OrderCreated,
            fromState: null,
            toState: OrderStatus.Created,
            eventData: JsonSerializer.Serialize(new { order.TotalAmount }),
            now));

        return order;
    }

    // Applies a state transition triggered by consuming `eventType`, rejecting
    // any transition not in OrderStateMachine and recording an OrderEvents
    // row for every attempt that succeeds — see docs/SPEC.md Order State
    // Machine table.
    public void Transition(OrderStatus to, string eventType, string eventData, DateTimeOffset now)
    {
        if (!OrderStateMachine.CanTransition(Status, to))
        {
            throw new InvalidOperationException(
                $"Cannot transition order {OrderId} from {Status} to {to} via {eventType}.");
        }

        var from = Status;
        Status = to;
        UpdatedAt = now;

        _orderEvents.Add(new OrderEvent(Guid.NewGuid(), OrderId, eventType, from, to, eventData, now));
    }

    // Records an audit-log entry for a consumed event that carries no state
    // transition of its own (InventoryReleased — task 9) — FromState/ToState
    // both equal the current, unchanged Status.
    public void RecordAudit(string eventType, string eventData, DateTimeOffset now)
    {
        _orderEvents.Add(new OrderEvent(Guid.NewGuid(), OrderId, eventType, Status, Status, eventData, now));
    }
}

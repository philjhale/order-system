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

    public static Order Create(
        Guid orderId,
        Guid userId,
        IReadOnlyList<NewOrderItem> items,
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
            ShippingAddress = shippingAddress,
            PaymentMethod = paymentMethod,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var item in items)
        {
            order._items.Add(new OrderItem(orderId, item.ProductId, item.Quantity, item.UnitPrice));
        }

        order.TotalAmount = order._items.Sum(i => i.Subtotal);

        order._orderEvents.Add(new OrderEvent(
            Guid.NewGuid(),
            orderId,
            eventType: "OrderCreated",
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
}

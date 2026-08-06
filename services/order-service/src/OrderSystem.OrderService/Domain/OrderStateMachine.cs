using OrderSystem.Contracts;

namespace OrderSystem.OrderService.Domain;

// Encodes only the automated transitions from the spec's Order State Machine
// table (docs/SPEC.md). RefundPending/Refunded are intentionally absent —
// the return/refund flow is out of scope for this MVP, and customer-initiated
// cancellation from Reserved/Confirmed is out of scope too.
public static class OrderStateMachine
{
    private static readonly IReadOnlyDictionary<OrderStatus, IReadOnlySet<OrderStatus>> AllowedTransitions =
        new Dictionary<OrderStatus, IReadOnlySet<OrderStatus>>
        {
            [OrderStatus.Created] = new HashSet<OrderStatus> { OrderStatus.Reserved, OrderStatus.Cancelled },
            [OrderStatus.Reserved] = new HashSet<OrderStatus> { OrderStatus.Confirmed, OrderStatus.Cancelled },
            [OrderStatus.Confirmed] = new HashSet<OrderStatus>(),
            [OrderStatus.Shipped] = new HashSet<OrderStatus> { OrderStatus.Delivered },
            [OrderStatus.Delivered] = new HashSet<OrderStatus>(),
            [OrderStatus.Cancelled] = new HashSet<OrderStatus>(),
        };

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
}

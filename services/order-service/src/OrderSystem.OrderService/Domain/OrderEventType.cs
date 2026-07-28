namespace OrderSystem.OrderService.Domain;

// Names must match the corresponding event DTO in
// shared/OrderSystem.Contracts/Events/*.cs — OrderEvent.EventType is
// audit-log metadata, not a dispatch key, but keeping it a compiler-checked
// constant instead of a literal avoids silent typos/divergence across the
// call sites in the event consumers.
public static class OrderEventType
{
    public const string OrderCreated = nameof(OrderCreated);
    public const string OrderCancelled = nameof(OrderCancelled);
    public const string OrderConfirmed = nameof(OrderConfirmed);
    public const string OrderShipped = nameof(OrderShipped);
    public const string OrderDelivered = nameof(OrderDelivered);
    public const string InventoryReserved = nameof(InventoryReserved);
    public const string InventoryFailed = nameof(InventoryFailed);
    public const string InventoryReleased = nameof(InventoryReleased);
    public const string PaymentCompleted = nameof(PaymentCompleted);
    public const string PaymentFailed = nameof(PaymentFailed);
    public const string PaymentRefunded = nameof(PaymentRefunded);
}

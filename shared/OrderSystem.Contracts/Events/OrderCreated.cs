namespace OrderSystem.Contracts.Events;

public sealed record OrderCreated(
    Guid OrderId,
    IReadOnlyList<OrderItemPayload> Items,
    decimal TotalAmount,
    string PaymentMethod);

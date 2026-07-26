namespace OrderSystem.OrderService.Domain;

// Input shape for Order.Create — deliberately separate from
// OrderSystem.Contracts.Events.OrderItemPayload, which is the wire shape of
// the OrderCreated event, not the domain's creation input.
public sealed record NewOrderItem(string ProductId, int Quantity, decimal UnitPrice);

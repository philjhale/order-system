namespace OrderSystem.OrderService.Api;

public sealed record CreateOrderRequest(
    Guid UserId,
    IReadOnlyList<CreateOrderItemRequest> Items,
    decimal TotalAmount,
    string ShippingAddress,
    string PaymentMethod);

public sealed record CreateOrderItemRequest(string ProductId, int Quantity, decimal UnitPrice);

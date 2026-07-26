using OrderSystem.Contracts;
using OrderSystem.OrderService.Domain;

namespace OrderSystem.OrderService.Api;

public sealed record OrderResponse(
    Guid OrderId,
    Guid UserId,
    OrderStatus Status,
    decimal TotalAmount,
    string ShippingAddress,
    string PaymentMethod,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderItemResponse> Items)
{
    public static OrderResponse FromDomain(Order order) => new(
        order.OrderId,
        order.UserId,
        order.Status,
        order.TotalAmount,
        order.ShippingAddress,
        order.PaymentMethod,
        order.CreatedAt,
        order.UpdatedAt,
        [.. order.Items.Select(OrderItemResponse.FromDomain)]);
}

public sealed record OrderItemResponse(string ProductId, int Quantity, decimal UnitPrice, decimal Subtotal)
{
    public static OrderItemResponse FromDomain(OrderItem item) => new(
        item.ProductId, item.Quantity, item.UnitPrice, item.Subtotal);
}

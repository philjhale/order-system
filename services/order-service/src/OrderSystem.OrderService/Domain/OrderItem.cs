namespace OrderSystem.OrderService.Domain;

public sealed class OrderItem
{
    public Guid OrderId { get; private set; }
    public string ProductId { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Subtotal { get; private set; }

    private OrderItem() { } // EF Core

    internal OrderItem(Guid orderId, string productId, int quantity, decimal unitPrice)
    {
        OrderId = orderId;
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Subtotal = quantity * unitPrice;
    }
}

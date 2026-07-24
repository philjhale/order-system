namespace OrderSystem.Contracts.Events;

public sealed record InventoryReserved(Guid OrderId, decimal TotalAmount, string PaymentMethod);

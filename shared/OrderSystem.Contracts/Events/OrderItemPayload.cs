namespace OrderSystem.Contracts.Events;

public sealed record OrderItemPayload(string ProductId, int Quantity, decimal UnitPrice);

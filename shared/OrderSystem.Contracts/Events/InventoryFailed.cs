namespace OrderSystem.Contracts.Events;

public sealed record InventoryFailed(Guid OrderId, InventoryFailureReason Reason);

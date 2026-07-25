namespace OrderSystem.Contracts.Events;

public sealed record OrderCancelled(Guid OrderId, OrderCancellationReason Reason);

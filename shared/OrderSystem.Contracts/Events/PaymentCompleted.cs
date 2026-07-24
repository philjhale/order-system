namespace OrderSystem.Contracts.Events;

public sealed record PaymentCompleted(Guid OrderId, Guid PaymentId);

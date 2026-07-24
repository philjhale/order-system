namespace OrderSystem.Contracts.Events;

// Reason is an opaque diagnostic string, not a fixed vocabulary — no
// functional requirement branches on it (see spec's Events section).
public sealed record PaymentFailed(Guid OrderId, string Reason);

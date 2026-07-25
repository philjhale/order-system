namespace OrderSystem.Contracts.Events;

// Defined for completeness only — no flow in this MVP produces it (see
// spec's Events section: customer-initiated cancellation is out of scope).
public sealed record PaymentRefunded(Guid OrderId, Guid PaymentId);

namespace OrderSystem.Contracts;

public enum PaymentStatus
{
    Pending,
    Completed,
    Failed,

    // Unused this MVP — refunds are out of scope.
    Refunded,
}

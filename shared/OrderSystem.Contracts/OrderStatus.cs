namespace OrderSystem.Contracts;

public enum OrderStatus
{
    Created,
    Reserved,
    Confirmed,
    Shipped,
    Delivered,
    Cancelled,

    // Unused this MVP — refunds are out of scope. Kept because the source
    // article's state machine defines them.
    RefundPending,
    Refunded,
}

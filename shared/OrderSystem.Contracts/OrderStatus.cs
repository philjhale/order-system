using System.Text.Json.Serialization;

namespace OrderSystem.Contracts;

// String-serialized: these enums cross service/deployment boundaries on the
// wire, and a reordered int-backed member would silently corrupt already
// in-flight or already-persisted events with no compile-time signal.
[JsonConverter(typeof(JsonStringEnumConverter))]
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

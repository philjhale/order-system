using System.Text.Json.Serialization;

namespace OrderSystem.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentStatus
{
    Pending,
    Completed,
    Failed,

    // Unused this MVP — refunds are out of scope.
    Refunded,
}

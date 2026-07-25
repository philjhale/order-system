using System.Text.Json.Serialization;

namespace OrderSystem.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrderCancellationReason
{
    PaymentFailed,
}

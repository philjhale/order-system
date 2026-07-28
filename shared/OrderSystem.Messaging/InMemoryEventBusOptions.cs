namespace OrderSystem.Messaging;

public sealed class InMemoryEventBusOptions
{
    /// <summary>Matches the Service Bus subscription MaxDeliveryCount this implementation mirrors.</summary>
    public int MaxDeliveryCount { get; init; } = 10;

    /// <summary>How long an abandoned message waits before its next delivery attempt.</summary>
    public TimeSpan RedeliveryDelay { get; init; } = TimeSpan.FromSeconds(2);
}

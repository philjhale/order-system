namespace OrderSystem.Messaging;

public sealed class ServiceBusEventBusOptions
{
    /// <summary>e.g. "order-system.servicebus.windows.net" — passwordless, no connection string.</summary>
    public required string FullyQualifiedNamespace { get; init; }

    /// <summary>
    /// How far in the future an abandoned message's redelivery is scheduled. Service
    /// Bus's own Abandon redelivers instantly, which would spin-loop a
    /// precondition-not-yet-met consumer (see IEventSubscriber); this implementation
    /// completes the original message and sends a scheduled clone instead.
    /// </summary>
    public TimeSpan RedeliveryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Matches the Service Bus subscription's own MaxDeliveryCount. Tracked
    /// separately here because completing+recloning on Abandon resets Service Bus's
    /// native per-message delivery count, so the subscription's own MaxDeliveryCount
    /// would otherwise never fire for a genuinely poison message.
    /// </summary>
    public int MaxDeliveryCount { get; init; } = 10;
}

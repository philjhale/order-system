namespace OrderSystem.Messaging;

public sealed class ServiceBusEventBusOptions
{
    /// <summary>e.g. "order-system.servicebus.windows.net" — passwordless, no connection string.</summary>
    public required string FullyQualifiedNamespace { get; init; }

    /// <summary>
    /// Client id of this service's own user-assigned managed identity. Required whenever the
    /// host has more than one user-assigned identity attached (e.g. an ACR-pull identity
    /// alongside this service's own) — without it, DefaultAzureCredential's managed-identity
    /// probe has no way to pick which identity to authenticate as and fails with
    /// "Unable to load the proper Managed Identity".
    /// </summary>
    public string? ManagedIdentityClientId { get; init; }

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

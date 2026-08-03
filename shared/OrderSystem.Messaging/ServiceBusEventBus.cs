using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using OrderSystem.Contracts.Events;

namespace OrderSystem.Messaging;

/// <summary>
/// Production IEventPublisher/IEventSubscriber wiring: Azure Service Bus, one topic
/// per event type (named after the event's CLR type, e.g. "OrderCreated"), session id
/// = OrderId for per-order ordering. Authenticates via DefaultAzureCredential against
/// each service's own user-assigned managed identity, granted Service Bus data-plane
/// RBAC on the shared namespace — no connection string or secret anywhere.
/// </summary>
public sealed class ServiceBusEventBus : IEventPublisher, IEventSubscriber, IAsyncDisposable
{
    // Enum contracts carry their own [JsonConverter(typeof(JsonStringEnumConverter))]
    // (see OrderStatus.cs) so no global enum converter is needed here.
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private readonly ServiceBusEventBusOptions _options;
    private readonly ServiceBusClient _client;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public ServiceBusEventBus(ServiceBusEventBusOptions options)
    {
        _options = options;
        var credential = new DefaultAzureCredential(
            new DefaultAzureCredentialOptions { ManagedIdentityClientId = options.ManagedIdentityClientId });
        _client = new ServiceBusClient(options.FullyQualifiedNamespace, credential);
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IOrderScopedEvent
    {
        var sender = GetSender<TEvent>();
        var message = CreateMessage(@event);
        await sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IAsyncDisposable> SubscribeAsync<TEvent>(
        string subscriptionName,
        EventMessageHandler<TEvent> handler,
        CancellationToken cancellationToken = default)
        where TEvent : class, IOrderScopedEvent
    {
        var topic = TopicName<TEvent>();
        var processor = _client.CreateSessionProcessor(topic, subscriptionName, new ServiceBusSessionProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentSessions = Environment.ProcessorCount,
            MaxConcurrentCallsPerSession = 1, // preserves per-session (per-OrderId) ordering
        });

        processor.ProcessMessageAsync += args => HandleMessageAsync(args, handler);
        processor.ProcessErrorAsync += _ => Task.CompletedTask;

        await processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessorHandle(processor);
    }

    private async Task HandleMessageAsync<TEvent>(ProcessSessionMessageEventArgs args, EventMessageHandler<TEvent> handler)
        where TEvent : class, IOrderScopedEvent
    {
        var @event = JsonSerializer.Deserialize<TEvent>(args.Message.Body, SerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize message body as {typeof(TEvent).Name}.");

        MessageOutcome outcome;
        try
        {
            outcome = await handler(@event, args.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            outcome = MessageOutcome.Abandon;
        }

        switch (outcome)
        {
            case MessageOutcome.Complete:
                await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
                break;

            case MessageOutcome.DeadLetter:
                await args.DeadLetterMessageAsync(args.Message, cancellationToken: args.CancellationToken).ConfigureAwait(false);
                break;

            case MessageOutcome.Abandon:
            default:
                await AbandonWithScheduledRedeliveryAsync(args, @event).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Completes the original message and schedules a redelivered clone a few seconds
    /// out, rather than a native Abandon (which redelivers instantly and would
    /// spin-loop a precondition-not-yet-met consumer). Completing resets Service Bus's
    /// own per-message delivery count, so this implementation tracks the logical
    /// attempt count itself (an application property carried across clones) and
    /// dead-letters directly once MaxDeliveryCount is exceeded — the subscription's
    /// native MaxDeliveryCount would otherwise never fire, since it never sees more
    /// than one delivery attempt per clone.
    /// </summary>
    private async Task AbandonWithScheduledRedeliveryAsync<TEvent>(ProcessSessionMessageEventArgs args, TEvent @event)
        where TEvent : class, IOrderScopedEvent
    {
        var deliveryCount = args.Message.ApplicationProperties.TryGetValue(DeliveryCountPropertyName, out var raw)
            ? (int)raw + 1
            : 1;

        if (deliveryCount >= _options.MaxDeliveryCount)
        {
            await args.DeadLetterMessageAsync(args.Message, cancellationToken: args.CancellationToken).ConfigureAwait(false);
            return;
        }

        var clone = CreateMessage(@event);
        clone.ApplicationProperties[DeliveryCountPropertyName] = deliveryCount;
        clone.ScheduledEnqueueTime = DateTimeOffset.UtcNow + _options.RedeliveryDelay;
        var sender = GetSender<TEvent>();
        await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
        await sender.SendMessageAsync(clone, args.CancellationToken).ConfigureAwait(false);
    }

    private const string DeliveryCountPropertyName = "OrderSystem-AbandonDeliveryCount";

    private static ServiceBusMessage CreateMessage<TEvent>(TEvent @event)
        where TEvent : class, IOrderScopedEvent
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(@event, SerializerOptions);
        return new ServiceBusMessage(body)
        {
            SessionId = @event.OrderId.ToString(),
            ContentType = "application/json",
        };
    }

    private ServiceBusSender GetSender<TEvent>() =>
        _senders.GetOrAdd(TopicName<TEvent>(), _client.CreateSender);

    private static string TopicName<TEvent>() => typeof(TEvent).Name;

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync().ConfigureAwait(false);
        }

        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class ProcessorHandle(ServiceBusSessionProcessor processor) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await processor.StopProcessingAsync().ConfigureAwait(false);
            await processor.DisposeAsync().ConfigureAwait(false);
        }
    }
}

using System.Collections.Concurrent;
using OrderSystem.Contracts.Events;

namespace OrderSystem.Messaging;

/// <summary>
/// In-process stand-in for the Azure Service Bus topic/subscription model, for unit
/// tests and local `dotnet run` (task 3). Not used in production — see
/// ServiceBusEventBus for the real wiring.
///
/// Mirrors session-based ordering: events for the same OrderId are delivered to a
/// given subscription strictly in publish order, one at a time. Different OrderIds are
/// independent and may be processed concurrently. Each subscription of a topic gets
/// its own copy of every published event (fan-out), matching Service Bus semantics.
/// </summary>
public sealed class InMemoryEventBus : IEventPublisher, IEventSubscriber
{
    private readonly InMemoryEventBusOptions _options;
    private readonly object _topicsLock = new();

    // topic (event type name) -> subscription name -> subscription
    private readonly Dictionary<string, Dictionary<string, ITopicSubscription>> _topics = new();

    public InMemoryEventBus(InMemoryEventBusOptions? options = null)
    {
        _options = options ?? new InMemoryEventBusOptions();
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IOrderScopedEvent
    {
        var topic = TopicName<TEvent>();
        List<ITopicSubscription>? subscriptions = null;
        lock (_topicsLock)
        {
            if (_topics.TryGetValue(topic, out var subs))
            {
                subscriptions = [.. subs.Values];
            }
        }

        if (subscriptions is not null)
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Enqueue(@event.OrderId, @event);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IAsyncDisposable> SubscribeAsync<TEvent>(
        string subscriptionName,
        EventMessageHandler<TEvent> handler,
        CancellationToken cancellationToken = default)
        where TEvent : class, IOrderScopedEvent
    {
        var topic = TopicName<TEvent>();
        var subscription = new TopicSubscription<TEvent>(handler, _options, cancellationToken);

        lock (_topicsLock)
        {
            if (!_topics.TryGetValue(topic, out var subs))
            {
                subs = new Dictionary<string, ITopicSubscription>();
                _topics[topic] = subs;
            }

            subs[subscriptionName] = subscription;
        }

        return Task.FromResult<IAsyncDisposable>(subscription);
    }

    private static string TopicName<TEvent>() => typeof(TEvent).Name;

    private interface ITopicSubscription
    {
        void Enqueue(Guid orderId, object @event);
    }

    /// <summary>
    /// One subscription's view of a topic. Runs one dispatch loop per OrderId
    /// ("session"), started lazily on first enqueue and stopped once its queue drains,
    /// so idle sessions don't hold a running Task.
    /// </summary>
    private sealed class TopicSubscription<TEvent> : ITopicSubscription, IAsyncDisposable
        where TEvent : class, IOrderScopedEvent
    {
        private readonly EventMessageHandler<TEvent> _handler;
        private readonly InMemoryEventBusOptions _options;
        private readonly CancellationTokenSource _cts;
        private readonly ConcurrentDictionary<Guid, SessionQueue> _sessions = new();

        public TopicSubscription(EventMessageHandler<TEvent> handler, InMemoryEventBusOptions options, CancellationToken externalToken)
        {
            _handler = handler;
            _options = options;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        }

        public void Enqueue(Guid orderId, object @event)
        {
            var session = _sessions.GetOrAdd(orderId, static _ => new SessionQueue());
            var startPump = session.Enqueue(new DeliveryAttempt((TEvent)@event));
            if (startPump)
            {
                _ = Task.Run(() => PumpAsync(session), CancellationToken.None);
            }
        }

        private async Task PumpAsync(SessionQueue session)
        {
            while (!_cts.IsCancellationRequested)
            {
                var (attempt, delay) = session.TryTakeDue();
                if (attempt is null)
                {
                    if (delay is null)
                    {
                        // Queue drained; pump exits, next Enqueue restarts it.
                        return;
                    }

                    try
                    {
                        await Task.Delay(delay.Value, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    continue;
                }

                MessageOutcome outcome;
                try
                {
                    outcome = await _handler((TEvent)attempt.Event, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    outcome = MessageOutcome.Abandon;
                }

                if (outcome == MessageOutcome.Abandon)
                {
                    attempt.DeliveryCount++;
                    if (attempt.DeliveryCount < _options.MaxDeliveryCount)
                    {
                        session.Requeue(attempt, DateTimeOffset.UtcNow + _options.RedeliveryDelay);
                        continue;
                    }
                    // Exceeded MaxDeliveryCount: dead-lettered, same as an explicit DeadLetter outcome.
                }
                // Complete or DeadLetter (or exhausted Abandon above): drop the message.
            }
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DeliveryAttempt(object @event)
    {
        public object Event { get; } = @event;
        public int DeliveryCount { get; set; }
    }

    /// <summary>Per-OrderId FIFO queue with strictly-sequential, delay-aware redelivery.</summary>
    private sealed class SessionQueue
    {
        private readonly object _lock = new();
        private readonly Queue<QueuedItem> _queue = new();

        /// <returns>true if the caller should start a new pump loop for this session.</returns>
        public bool Enqueue(DeliveryAttempt attempt)
        {
            lock (_lock)
            {
                var wasEmpty = _queue.Count == 0;
                _queue.Enqueue(new QueuedItem(attempt, DateTimeOffset.MinValue));
                return wasEmpty;
            }
        }

        public void Requeue(DeliveryAttempt attempt, DateTimeOffset dueAt)
        {
            lock (_lock)
            {
                _queue.Enqueue(new QueuedItem(attempt, dueAt));
            }
        }

        /// <summary>
        /// Takes the front item if it's due. If the queue is non-empty but the front
        /// item isn't due yet, returns the delay to wait before checking again (never
        /// skips ahead — that would break per-session ordering). Returns (null, null)
        /// when the queue is empty.
        /// </summary>
        public (DeliveryAttempt? Attempt, TimeSpan? WaitFor) TryTakeDue()
        {
            lock (_lock)
            {
                if (_queue.Count == 0) return (null, null);

                var front = _queue.Peek();
                var now = DateTimeOffset.UtcNow;
                if (front.DueAt > now)
                {
                    return (null, front.DueAt - now);
                }

                _queue.Dequeue();
                return (front.Attempt, null);
            }
        }

        private readonly record struct QueuedItem(DeliveryAttempt Attempt, DateTimeOffset DueAt);
    }
}

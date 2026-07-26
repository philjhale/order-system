using OrderSystem.Contracts.Events;
using OrderSystem.Messaging;

namespace OrderSystem.Messaging.Tests;

public class InMemoryEventBusTests
{
    [Fact]
    public async Task PublishThenSubscribe_DeliversEvent()
    {
        var bus = new InMemoryEventBus();
        var received = new TaskCompletionSource<OrderCreated>();

        await bus.SubscribeAsync<OrderCreated>("order-service", (evt, ct) =>
        {
            received.TrySetResult(evt);
            return Task.FromResult(MessageOutcome.Complete);
        });

        var orderId = Guid.NewGuid();
        var evt = new OrderCreated(orderId, [], 10m, "card");
        await bus.PublishAsync(evt);

        var delivered = await WaitAsync(received.Task);
        Assert.Equal(orderId, delivered.OrderId);
    }

    [Fact]
    public async Task Events_ForSameOrderId_AreDeliveredInPublishOrder()
    {
        var bus = new InMemoryEventBus();
        var orderId = Guid.NewGuid();
        var deliveryOrder = new List<int>();
        var allDelivered = new TaskCompletionSource();
        const int total = 20;

        await bus.SubscribeAsync<OrderShipped>("order-service", async (evt, ct) =>
        {
            // Random tiny delay so handlers can genuinely race if ordering isn't enforced.
            await Task.Delay(Random.Shared.Next(0, 5), ct);
            lock (deliveryOrder)
            {
                deliveryOrder.Add(deliveryOrder.Count);
                if (deliveryOrder.Count == total) allDelivered.TrySetResult();
            }
            return MessageOutcome.Complete;
        });

        for (var i = 0; i < total; i++)
        {
            await bus.PublishAsync(new OrderShipped(orderId));
        }

        await WaitAsync(allDelivered.Task);
        Assert.Equal(Enumerable.Range(0, total), deliveryOrder);
    }

    [Fact]
    public async Task DifferentOrderIds_AreDeliveredToDifferentSessions_Independently()
    {
        var bus = new InMemoryEventBus();
        var seenOrderIds = new HashSet<Guid>();
        var allDelivered = new TaskCompletionSource();
        var orderIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

        await bus.SubscribeAsync<OrderDelivered>("order-service", (evt, ct) =>
        {
            lock (seenOrderIds)
            {
                seenOrderIds.Add(evt.OrderId);
                if (seenOrderIds.Count == orderIds.Count) allDelivered.TrySetResult();
            }
            return Task.FromResult(MessageOutcome.Complete);
        });

        foreach (var orderId in orderIds)
        {
            await bus.PublishAsync(new OrderDelivered(orderId));
        }

        await WaitAsync(allDelivered.Task);
        Assert.Equal(orderIds.ToHashSet(), seenOrderIds);
    }

    [Fact]
    public async Task AbandonedMessage_IsRedeliveredAfterDelay_NotLost()
    {
        var options = new InMemoryEventBusOptions { RedeliveryDelay = TimeSpan.FromMilliseconds(100) };
        var bus = new InMemoryEventBus(options);
        var attempts = new List<DateTimeOffset>();
        var secondAttempt = new TaskCompletionSource();

        await bus.SubscribeAsync<PaymentFailed>("order-service", (evt, ct) =>
        {
            lock (attempts)
            {
                attempts.Add(DateTimeOffset.UtcNow);
                if (attempts.Count == 1) return Task.FromResult(MessageOutcome.Abandon);
                secondAttempt.TrySetResult();
                return Task.FromResult(MessageOutcome.Complete);
            }
        });

        await bus.PublishAsync(new PaymentFailed(Guid.NewGuid(), "declined"));

        await WaitAsync(secondAttempt.Task);
        Assert.Equal(2, attempts.Count);
        Assert.True(attempts[1] - attempts[0] >= TimeSpan.FromMilliseconds(90));
    }

    [Fact]
    public async Task Message_ExceedingMaxDeliveryCount_IsDeadLettered_NotRedeliveredForever()
    {
        var options = new InMemoryEventBusOptions
        {
            MaxDeliveryCount = 3,
            RedeliveryDelay = TimeSpan.FromMilliseconds(10),
        };
        var bus = new InMemoryEventBus(options);
        var attemptCount = 0;
        var noMoreDeliveries = new TaskCompletionSource();

        await bus.SubscribeAsync<PaymentFailed>("order-service", (evt, ct) =>
        {
            Interlocked.Increment(ref attemptCount);
            return Task.FromResult(MessageOutcome.Abandon);
        });

        await bus.PublishAsync(new PaymentFailed(Guid.NewGuid(), "declined"));

        // Give it plenty of time to exhaust delivery attempts and settle.
        await Task.Delay(500);
        Assert.Equal(3, attemptCount);
    }

    [Fact]
    public async Task Subscription_DisposeAsync_CalledTwice_DoesNotThrow()
    {
        // WebApplicationFactory's IDisposable/IAsyncDisposable dual teardown path has
        // been observed to dispose a hosted service's subscriptions more than once —
        // DisposeAsync must be idempotent rather than hitting an already-disposed
        // CancellationTokenSource on the second call.
        var bus = new InMemoryEventBus();
        var subscription = await bus.SubscribeAsync<OrderShipped>(
            "order-service", (_, _) => Task.FromResult(MessageOutcome.Complete));

        await subscription.DisposeAsync();
        await subscription.DisposeAsync();
    }

    [Fact]
    public async Task Subscription_DisposeAsync_WaitsForAnInFlightHandlerToFinish()
    {
        var bus = new InMemoryEventBus();
        var handlerStarted = new TaskCompletionSource();
        var releaseHandler = new TaskCompletionSource();
        var handlerCompleted = false;

        var subscription = await bus.SubscribeAsync<OrderShipped>("order-service", async (_, _) =>
        {
            handlerStarted.TrySetResult();
            await releaseHandler.Task;
            handlerCompleted = true;
            return MessageOutcome.Complete;
        });

        await bus.PublishAsync(new OrderShipped(Guid.NewGuid()));
        await WaitAsync(handlerStarted.Task);

        var disposeTask = subscription.DisposeAsync().AsTask();
        Assert.False(disposeTask.IsCompleted); // still draining the in-flight handler

        releaseHandler.TrySetResult();
        await WaitAsync(disposeTask);
        Assert.True(handlerCompleted);
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, int timeoutMs = 5000)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
        Assert.True(completed == task, "Timed out waiting for event delivery.");
        return await task;
    }

    private static async Task WaitAsync(Task task, int timeoutMs = 5000)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
        Assert.True(completed == task, "Timed out waiting for event delivery.");
        await task;
    }
}

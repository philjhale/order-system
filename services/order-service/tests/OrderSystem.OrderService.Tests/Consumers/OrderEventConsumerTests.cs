using Microsoft.EntityFrameworkCore;
using OrderSystem.Contracts;
using OrderSystem.Contracts.Events;
using OrderSystem.Messaging;
using OrderSystem.OrderService.Consumers;
using OrderSystem.OrderService.Domain;
using OrderSystem.OrderService.Persistence;

namespace OrderSystem.OrderService.Tests.Consumers;

public sealed class OrderEventConsumerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly OrderDbContext _db;
    private readonly RecordingEventPublisher _publisher = new();
    private readonly OrderEventConsumer _consumer;

    public OrderEventConsumerTests()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase($"order-event-consumer-tests-{Guid.NewGuid()}")
            .Options;
        _db = new OrderDbContext(options);
        _consumer = new OrderEventConsumer(_db, _publisher, new FakeTimeProvider(Now));
    }

    public void Dispose() => _db.Dispose();

    private Order SeedOrder(OrderStatus status)
    {
        var order = Order.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            [new NewOrderItem("sku-1", 1, 10.00m)],
            totalAmount: 10.00m,
            shippingAddress: "{}",
            paymentMethod: "card",
            Now);

        if (status != OrderStatus.Created)
        {
            // Walk through the legal path to the requested seed state so
            // OrderStateMachine's own rules aren't bypassed by the test setup.
            if (status is OrderStatus.Reserved or OrderStatus.Confirmed or OrderStatus.Shipped or OrderStatus.Delivered)
            {
                order.Transition(OrderStatus.Reserved, "seed", "{}", Now);
            }

            if (status is OrderStatus.Confirmed or OrderStatus.Shipped or OrderStatus.Delivered)
            {
                order.Transition(OrderStatus.Confirmed, "seed", "{}", Now);
            }

            if (status is OrderStatus.Shipped or OrderStatus.Delivered)
            {
                order.Transition(OrderStatus.Shipped, "seed", "{}", Now);
            }

            if (status is OrderStatus.Delivered)
            {
                order.Transition(OrderStatus.Delivered, "seed", "{}", Now);
            }

            if (status is OrderStatus.Cancelled)
            {
                order.Transition(OrderStatus.Cancelled, "seed", "{}", Now);
            }
        }

        _db.Orders.Add(order);
        _db.SaveChanges();
        return order;
    }

    [Fact]
    public async Task InventoryReserved_moves_Created_order_to_Reserved_and_completes()
    {
        var order = SeedOrder(OrderStatus.Created);

        var outcome = await _consumer.HandleInventoryReservedAsync(
            new InventoryReserved(order.OrderId, 10.00m, "card"), CancellationToken.None);

        Assert.Equal(MessageOutcome.Complete, outcome);
        var reloaded = await _db.Orders.FindAsync(order.OrderId);
        Assert.Equal(OrderStatus.Reserved, reloaded!.Status);
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task InventoryReserved_redelivered_after_already_Reserved_is_a_no_op_Complete()
    {
        var order = SeedOrder(OrderStatus.Reserved);

        var outcome = await _consumer.HandleInventoryReservedAsync(
            new InventoryReserved(order.OrderId, 10.00m, "card"), CancellationToken.None);

        Assert.Equal(MessageOutcome.Complete, outcome);
        var reloaded = await _db.Orders.FindAsync(order.OrderId);
        Assert.Equal(2, reloaded!.OrderEvents.Count); // no duplicate audit row written for the redelivery
    }

    [Fact]
    public async Task InventoryReserved_arriving_before_its_precondition_is_abandoned_for_redelivery()
    {
        // Order not yet even Created's normal path — simulate a payment event racing
        // ahead by seeding a Confirmed order and replaying InventoryReserved onto it.
        var order = SeedOrder(OrderStatus.Confirmed);

        var outcome = await _consumer.HandleInventoryReservedAsync(
            new InventoryReserved(order.OrderId, 10.00m, "card"), CancellationToken.None);

        Assert.Equal(MessageOutcome.Abandon, outcome);
    }

    [Fact]
    public async Task InventoryReserved_for_an_unknown_order_is_abandoned()
    {
        var outcome = await _consumer.HandleInventoryReservedAsync(
            new InventoryReserved(Guid.NewGuid(), 10.00m, "card"), CancellationToken.None);

        Assert.Equal(MessageOutcome.Abandon, outcome);
    }

    [Fact]
    public async Task InventoryFailed_moves_Created_order_to_Cancelled_with_no_follow_up_publish()
    {
        var order = SeedOrder(OrderStatus.Created);

        var outcome = await _consumer.HandleInventoryFailedAsync(
            new InventoryFailed(order.OrderId, InventoryFailureReason.OutOfStock), CancellationToken.None);

        Assert.Equal(MessageOutcome.Complete, outcome);
        var reloaded = await _db.Orders.FindAsync(order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, reloaded!.Status);
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task PaymentCompleted_moves_Reserved_order_to_Confirmed_and_publishes_OrderConfirmed()
    {
        var order = SeedOrder(OrderStatus.Reserved);

        var outcome = await _consumer.HandlePaymentCompletedAsync(
            new PaymentCompleted(order.OrderId, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(MessageOutcome.Complete, outcome);
        var reloaded = await _db.Orders.FindAsync(order.OrderId);
        Assert.Equal(OrderStatus.Confirmed, reloaded!.Status);
        var published = Assert.Single(_publisher.Published);
        var confirmed = Assert.IsType<OrderConfirmed>(published);
        Assert.Equal(order.OrderId, confirmed.OrderId);
    }

    [Fact]
    public async Task PaymentCompleted_redelivered_after_already_Confirmed_republishes_OrderConfirmed_without_reapplying_the_transition()
    {
        // Redelivery is the only signal a handler gets that an earlier attempt's
        // follow-up publish may never have completed (it's a separate, non-atomic step
        // from the DB commit) — so it must retry the publish, not skip it. Every
        // downstream consumer already tolerates duplicate delivery.
        var order = SeedOrder(OrderStatus.Confirmed);

        var outcome = await _consumer.HandlePaymentCompletedAsync(
            new PaymentCompleted(order.OrderId, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(MessageOutcome.Complete, outcome);
        var published = Assert.Single(_publisher.Published);
        Assert.IsType<OrderConfirmed>(published);
        var reloaded = await _db.Orders.FindAsync(order.OrderId);
        Assert.Equal(3, reloaded!.OrderEvents.Count); // no duplicate audit row for the transition itself
    }

    [Fact]
    public async Task PaymentFailed_moves_Reserved_order_to_Cancelled_and_publishes_OrderCancelled()
    {
        var order = SeedOrder(OrderStatus.Reserved);

        var outcome = await _consumer.HandlePaymentFailedAsync(
            new PaymentFailed(order.OrderId, "card_declined"), CancellationToken.None);

        Assert.Equal(MessageOutcome.Complete, outcome);
        var reloaded = await _db.Orders.FindAsync(order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, reloaded!.Status);
        var published = Assert.Single(_publisher.Published);
        var cancelled = Assert.IsType<OrderCancelled>(published);
        Assert.Equal(order.OrderId, cancelled.OrderId);
        Assert.Equal(OrderCancellationReason.PaymentFailed, cancelled.Reason);
    }

    [Fact]
    public async Task OrderShipped_moves_Confirmed_order_to_Shipped()
    {
        var order = SeedOrder(OrderStatus.Confirmed);

        var outcome = await _consumer.HandleOrderShippedAsync(new OrderShipped(order.OrderId), CancellationToken.None);

        Assert.Equal(MessageOutcome.Complete, outcome);
        var reloaded = await _db.Orders.FindAsync(order.OrderId);
        Assert.Equal(OrderStatus.Shipped, reloaded!.Status);
    }

    [Fact]
    public async Task OrderDelivered_moves_Shipped_order_to_Delivered()
    {
        var order = SeedOrder(OrderStatus.Shipped);

        var outcome = await _consumer.HandleOrderDeliveredAsync(new OrderDelivered(order.OrderId), CancellationToken.None);

        Assert.Equal(MessageOutcome.Complete, outcome);
        var reloaded = await _db.Orders.FindAsync(order.OrderId);
        Assert.Equal(OrderStatus.Delivered, reloaded!.Status);
    }

    [Fact]
    public async Task InventoryReleased_records_an_audit_event_without_changing_status()
    {
        var order = SeedOrder(OrderStatus.Cancelled);

        var outcome = await _consumer.HandleInventoryReleasedAsync(new InventoryReleased(order.OrderId), CancellationToken.None);

        Assert.Equal(MessageOutcome.Complete, outcome);
        var reloaded = await _db.Orders.Include(o => o.OrderEvents).FirstAsync(o => o.OrderId == order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, reloaded.Status);
        Assert.Contains(reloaded.OrderEvents, e => e.EventType == OrderEventType.InventoryReleased);
    }

    [Fact]
    public async Task InventoryReleased_redelivered_after_already_recorded_does_not_add_a_duplicate_audit_row()
    {
        var order = SeedOrder(OrderStatus.Cancelled);
        await _consumer.HandleInventoryReleasedAsync(new InventoryReleased(order.OrderId), CancellationToken.None);

        var outcome = await _consumer.HandleInventoryReleasedAsync(new InventoryReleased(order.OrderId), CancellationToken.None);

        Assert.Equal(MessageOutcome.Complete, outcome);
        var reloaded = await _db.Orders.Include(o => o.OrderEvents).FirstAsync(o => o.OrderId == order.OrderId);
        Assert.Single(reloaded.OrderEvents, e => e.EventType == OrderEventType.InventoryReleased);
    }

    [Fact]
    public async Task InventoryReleased_for_an_unknown_order_is_abandoned()
    {
        var outcome = await _consumer.HandleInventoryReleasedAsync(new InventoryReleased(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(MessageOutcome.Abandon, outcome);
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        private readonly List<object> _published = [];
        public IReadOnlyList<object> Published => _published;

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, IOrderScopedEvent
        {
            _published.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

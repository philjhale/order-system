using System.Text.Json;
using OrderSystem.Contracts;
using OrderSystem.Contracts.Events;
using OrderSystem.Messaging;
using OrderSystem.OrderService.Domain;
using OrderSystem.OrderService.Persistence;

namespace OrderSystem.OrderService.Consumers;

/// <summary>
/// Applies the state transitions from docs/SPEC.md's Order State Machine table for every
/// event Order Service consumes (task 9). One instance is resolved per delivered message
/// from a fresh DI scope (see OrderEventConsumerHostedService) so the scoped OrderDbContext
/// is never shared across concurrent deliveries.
///
/// Idempotency: if the order is already in a transition's target state, the handler
/// completes without reapplying it or re-publishing any follow-up event, so redelivery of
/// an already-applied message is a safe no-op. If the order is in neither the expected
/// precondition state nor the target state, the precondition hasn't been reached yet (a
/// cross-topic race — see IEventSubscriber) and the message is abandoned for redelivery.
/// </summary>
public sealed class OrderEventConsumer(OrderDbContext db, IEventPublisher publisher, TimeProvider timeProvider)
{
    public Task<MessageOutcome> HandleInventoryReservedAsync(InventoryReserved @event, CancellationToken cancellationToken) =>
        ApplyTransitionAsync(
            @event.OrderId,
            precondition: OrderStatus.Created,
            target: OrderStatus.Reserved,
            OrderEventType.InventoryReserved,
            JsonSerializer.Serialize(@event),
            publishFollowUp: null,
            cancellationToken);

    public Task<MessageOutcome> HandleInventoryFailedAsync(InventoryFailed @event, CancellationToken cancellationToken) =>
        ApplyTransitionAsync(
            @event.OrderId,
            precondition: OrderStatus.Created,
            target: OrderStatus.Cancelled,
            OrderEventType.InventoryFailed,
            JsonSerializer.Serialize(@event),
            publishFollowUp: null,
            cancellationToken);

    public Task<MessageOutcome> HandlePaymentCompletedAsync(PaymentCompleted @event, CancellationToken cancellationToken) =>
        ApplyTransitionAsync(
            @event.OrderId,
            precondition: OrderStatus.Reserved,
            target: OrderStatus.Confirmed,
            OrderEventType.PaymentCompleted,
            JsonSerializer.Serialize(@event),
            publishFollowUp: (orderId, ct) => publisher.PublishAsync(new OrderConfirmed(orderId), ct),
            cancellationToken);

    // Per docs/SPEC.md's compensation path, OrderCancelled is only published here — on
    // InventoryFailed the order is cancelled but there is no reservation to release, so
    // Inventory Service has nothing to compensate and no OrderCancelled is needed.
    public Task<MessageOutcome> HandlePaymentFailedAsync(PaymentFailed @event, CancellationToken cancellationToken) =>
        ApplyTransitionAsync(
            @event.OrderId,
            precondition: OrderStatus.Reserved,
            target: OrderStatus.Cancelled,
            OrderEventType.PaymentFailed,
            JsonSerializer.Serialize(@event),
            publishFollowUp: (orderId, ct) => publisher.PublishAsync(
                new OrderCancelled(orderId, OrderCancellationReason.PaymentFailed), ct),
            cancellationToken);

    public Task<MessageOutcome> HandleOrderShippedAsync(OrderShipped @event, CancellationToken cancellationToken) =>
        ApplyTransitionAsync(
            @event.OrderId,
            precondition: OrderStatus.Confirmed,
            target: OrderStatus.Shipped,
            OrderEventType.OrderShipped,
            JsonSerializer.Serialize(@event),
            publishFollowUp: null,
            cancellationToken);

    public Task<MessageOutcome> HandleOrderDeliveredAsync(OrderDelivered @event, CancellationToken cancellationToken) =>
        ApplyTransitionAsync(
            @event.OrderId,
            precondition: OrderStatus.Shipped,
            target: OrderStatus.Delivered,
            OrderEventType.OrderDelivered,
            JsonSerializer.Serialize(@event),
            publishFollowUp: null,
            cancellationToken);

    // InventoryReleased carries no state transition of its own for Order Service — it's
    // consumed purely for the audit trail (tasks/todo.md task 9).
    public async Task<MessageOutcome> HandleInventoryReleasedAsync(InventoryReleased @event, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FindAsync([@event.OrderId], cancellationToken);
        if (order is null)
        {
            return MessageOutcome.Abandon;
        }

        order.RecordAudit(OrderEventType.InventoryReleased, JsonSerializer.Serialize(@event), timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return MessageOutcome.Complete;
    }

    private async Task<MessageOutcome> ApplyTransitionAsync(
        Guid orderId,
        OrderStatus precondition,
        OrderStatus target,
        string eventType,
        string eventData,
        Func<Guid, CancellationToken, Task>? publishFollowUp,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.FindAsync([orderId], cancellationToken);
        if (order is null)
        {
            return MessageOutcome.Abandon;
        }

        if (order.Status == target)
        {
            return MessageOutcome.Complete;
        }

        if (order.Status != precondition)
        {
            return MessageOutcome.Abandon;
        }

        order.Transition(target, eventType, eventData, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        if (publishFollowUp is not null)
        {
            await publishFollowUp(orderId, cancellationToken);
        }

        return MessageOutcome.Complete;
    }
}

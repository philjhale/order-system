using OrderSystem.Contracts;
using OrderSystem.OrderService.Domain;

namespace OrderSystem.OrderService.Tests.Domain;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Order CreateOrder() => Order.Create(
        Guid.NewGuid(),
        Guid.NewGuid(),
        [new NewOrderItem("sku-1", 2, 10.00m), new NewOrderItem("sku-2", 1, 5.50m)],
        totalAmount: 25.50m,
        shippingAddress: "{}",
        paymentMethod: "card",
        Now);

    [Fact]
    public void Create_starts_in_Created_state_with_the_submitted_total_and_an_audit_event()
    {
        var order = CreateOrder();

        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Equal(25.50m, order.TotalAmount);
        Assert.Equal(2, order.Items.Count);

        var orderEvent = Assert.Single(order.OrderEvents);
        Assert.Equal(OrderEventType.OrderCreated, orderEvent.EventType);
        Assert.Null(orderEvent.FromState);
        Assert.Equal(OrderStatus.Created, orderEvent.ToState);
    }

    [Fact]
    public void Create_rejects_an_order_with_no_items()
    {
        Assert.Throws<ArgumentException>(() => Order.Create(
            Guid.NewGuid(), Guid.NewGuid(), [], 0m, "{}", "card", Now));
    }

    [Fact]
    public void Transition_applies_a_legal_transition_and_records_an_audit_event()
    {
        var order = CreateOrder();
        var transitionTime = Now.AddMinutes(5);

        order.Transition(OrderStatus.Reserved, OrderEventType.InventoryReserved, "{}", transitionTime);

        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(transitionTime, order.UpdatedAt);

        Assert.Equal(2, order.OrderEvents.Count);
        var latestEvent = order.OrderEvents[^1];
        Assert.Equal(OrderEventType.InventoryReserved, latestEvent.EventType);
        Assert.Equal(OrderStatus.Created, latestEvent.FromState);
        Assert.Equal(OrderStatus.Reserved, latestEvent.ToState);
    }

    [Fact]
    public void Transition_rejects_an_illegal_transition_and_records_no_event()
    {
        var order = CreateOrder();

        var ex = Assert.Throws<InvalidOperationException>(
            () => order.Transition(OrderStatus.Confirmed, OrderEventType.PaymentCompleted, "{}", Now.AddMinutes(5)));

        Assert.Contains("Created", ex.Message);
        Assert.Contains("Confirmed", ex.Message);
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Single(order.OrderEvents);
    }
}

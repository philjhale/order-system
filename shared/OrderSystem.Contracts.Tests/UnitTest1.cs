using System.Text.Json;
using OrderSystem.Contracts.Events;

namespace OrderSystem.Contracts.Tests;

public class EventContractSerializationTests
{
    [Fact]
    public void OrderCreated_RoundTrips()
    {
        var original = new OrderCreated(
            Guid.NewGuid(),
            [new OrderItemPayload("sku-1", 2, 9.99m)],
            19.98m,
            "credit-card");

        var roundTripped = RoundTrip(original);

        Assert.Equal(original.OrderId, roundTripped.OrderId);
        Assert.Equal(original.TotalAmount, roundTripped.TotalAmount);
        Assert.Equal(original.PaymentMethod, roundTripped.PaymentMethod);
        Assert.Equal(original.Items, roundTripped.Items);
    }

    [Fact]
    public void InventoryReserved_RoundTrips()
    {
        var original = new InventoryReserved(Guid.NewGuid(), 19.98m, "credit-card");

        Assert.Equal(original, RoundTrip(original));
    }

    [Fact]
    public void InventoryFailed_RoundTrips()
    {
        var original = new InventoryFailed(Guid.NewGuid(), InventoryFailureReason.OutOfStock);

        Assert.Equal(original, RoundTrip(original));
    }

    [Fact]
    public void PaymentCompleted_RoundTrips()
    {
        var original = new PaymentCompleted(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(original, RoundTrip(original));
    }

    [Fact]
    public void PaymentFailed_RoundTrips()
    {
        var original = new PaymentFailed(Guid.NewGuid(), "gateway timeout");

        Assert.Equal(original, RoundTrip(original));
    }

    [Fact]
    public void OrderConfirmed_RoundTrips()
    {
        var original = new OrderConfirmed(Guid.NewGuid());

        Assert.Equal(original, RoundTrip(original));
    }

    [Fact]
    public void OrderCancelled_RoundTrips()
    {
        var original = new OrderCancelled(Guid.NewGuid(), OrderCancellationReason.PaymentFailed);

        Assert.Equal(original, RoundTrip(original));
    }

    [Fact]
    public void InventoryReleased_RoundTrips()
    {
        var original = new InventoryReleased(Guid.NewGuid());

        Assert.Equal(original, RoundTrip(original));
    }

    [Fact]
    public void PaymentRefunded_RoundTrips()
    {
        var original = new PaymentRefunded(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(original, RoundTrip(original));
    }

    [Fact]
    public void OrderShipped_RoundTrips()
    {
        var original = new OrderShipped(Guid.NewGuid());

        Assert.Equal(original, RoundTrip(original));
    }

    [Fact]
    public void OrderDelivered_RoundTrips()
    {
        var original = new OrderDelivered(Guid.NewGuid());

        Assert.Equal(original, RoundTrip(original));
    }

    [Fact]
    public void InventoryFailed_SerializesReasonAsString_NotInt()
    {
        var json = JsonSerializer.Serialize(new InventoryFailed(Guid.NewGuid(), InventoryFailureReason.OutOfStock));

        Assert.Contains("\"OutOfStock\"", json);
    }

    [Fact]
    public void OrderCancelled_SerializesReasonAsString_NotInt()
    {
        var json = JsonSerializer.Serialize(new OrderCancelled(Guid.NewGuid(), OrderCancellationReason.PaymentFailed));

        Assert.Contains("\"PaymentFailed\"", json);
    }

    private static T RoundTrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<T>(json)
            ?? throw new InvalidOperationException("Deserialization returned null.");
    }
}

public class OrderStatusEnumTests
{
    [Theory]
    [InlineData(OrderStatus.Created)]
    [InlineData(OrderStatus.Reserved)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.RefundPending)]
    [InlineData(OrderStatus.Refunded)]
    public void HasExpectedMembers(OrderStatus status)
    {
        Assert.True(Enum.IsDefined(status));
    }

    [Fact]
    public void HasExactlyEightMembers()
    {
        Assert.Equal(8, Enum.GetValues<OrderStatus>().Length);
    }
}

public class PaymentStatusEnumTests
{
    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public void HasExpectedMembers(PaymentStatus status)
    {
        Assert.True(Enum.IsDefined(status));
    }

    [Fact]
    public void HasExactlyFourMembers()
    {
        Assert.Equal(4, Enum.GetValues<PaymentStatus>().Length);
    }
}

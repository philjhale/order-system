using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OrderSystem.Contracts;
using OrderSystem.Contracts.Events;
using OrderSystem.Messaging;
using OrderSystem.OrderService.Api;

namespace OrderSystem.OrderService.Tests.Api;

// End-to-end through the real message bus: publishes an event the same way
// Inventory/Payment/Fulfillment Service would, via the IEventPublisher registered in
// this host's own DI container, then confirms OrderEventConsumerHostedService's
// subscription picked it up and applied the state transition.
public sealed class OrderEventConsumerIntegrationTests : IClassFixture<OrderEventConsumerApiFactory>
{
    private readonly OrderEventConsumerApiFactory _factory;
    private readonly HttpClient _client;

    public OrderEventConsumerIntegrationTests(OrderEventConsumerApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CreateOrderRequest ValidRequest() => new(
        UserId: Guid.NewGuid(),
        Items: [new CreateOrderItemRequest("sku-1", 1, 10.00m)],
        TotalAmount: 10.00m,
        ShippingAddress: "1 Test Street",
        PaymentMethod: "card-ending-1234");

    private async Task<Guid> CreateOrderAsync()
    {
        var response = await _client.PostAsJsonAsync("/orders", ValidRequest());
        var body = await response.Content.ReadFromJsonAsync<OrderResponse>();
        return body!.OrderId;
    }

    private async Task<OrderResponse> WaitForStatusAsync(Guid orderId, OrderStatus expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var response = await _client.GetAsync($"/orders/{orderId}");
            var body = (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
            if (body.Status == expected)
            {
                return body;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Order {orderId} did not reach status {expected} in time.");
    }

    [Fact]
    public async Task InventoryReserved_event_moves_the_order_to_Reserved()
    {
        var orderId = await CreateOrderAsync();
        var publisher = _factory.Services.GetRequiredService<IEventPublisher>();

        await publisher.PublishAsync(new InventoryReserved(orderId, 10.00m, "card-ending-1234"));

        var order = await WaitForStatusAsync(orderId, OrderStatus.Reserved);
        Assert.Equal(OrderStatus.Reserved, order.Status);
    }

    [Fact]
    public async Task PaymentCompleted_event_moves_a_Reserved_order_to_Confirmed()
    {
        var orderId = await CreateOrderAsync();
        var publisher = _factory.Services.GetRequiredService<IEventPublisher>();

        await publisher.PublishAsync(new InventoryReserved(orderId, 10.00m, "card-ending-1234"));
        await WaitForStatusAsync(orderId, OrderStatus.Reserved);

        await publisher.PublishAsync(new PaymentCompleted(orderId, Guid.NewGuid()));

        var order = await WaitForStatusAsync(orderId, OrderStatus.Confirmed);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }
}

using System.Net;
using System.Net.Http.Json;
using OrderSystem.Contracts;
using OrderSystem.Contracts.Events;
using OrderSystem.OrderService.Api;

namespace OrderSystem.OrderService.Tests.Api;

public sealed class OrderEndpointsTests : IClassFixture<OrderApiFactory>
{
    private readonly OrderApiFactory _factory;
    private readonly HttpClient _client;

    public OrderEndpointsTests(OrderApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CreateOrderRequest ValidRequest() => new(
        UserId: Guid.NewGuid(),
        Items: [new CreateOrderItemRequest("sku-1", 2, 9.99m)],
        TotalAmount: 19.98m,
        ShippingAddress: "1 Test Street",
        PaymentMethod: "card-ending-1234");

    [Fact]
    public async Task POST_orders_creates_the_order_and_returns_201_with_a_Location_header()
    {
        var request = ValidRequest();

        var response = await _client.PostAsJsonAsync("/orders", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.OrderId);
        Assert.Equal(request.UserId, body.UserId);
        Assert.Equal(OrderStatus.Created, body.Status);
        Assert.Equal(request.TotalAmount, body.TotalAmount);
        Assert.Equal(request.ShippingAddress, body.ShippingAddress);
        Assert.Equal(request.PaymentMethod, body.PaymentMethod);
        Assert.Single(body.Items);
        Assert.Equal("sku-1", body.Items[0].ProductId);
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/orders/{body.OrderId}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task POST_orders_publishes_an_OrderCreated_event_matching_the_persisted_order()
    {
        var request = ValidRequest();

        var response = await _client.PostAsJsonAsync("/orders", request);
        var body = await response.Content.ReadFromJsonAsync<OrderResponse>();

        var published = Assert.Single(_factory.Publisher.Published.OfType<OrderCreated>(),
            e => e.OrderId == body!.OrderId);
        Assert.Equal(request.TotalAmount, published.TotalAmount);
        Assert.Equal(request.PaymentMethod, published.PaymentMethod);
        Assert.Single(published.Items);
        Assert.Equal("sku-1", published.Items[0].ProductId);
        Assert.Equal(2, published.Items[0].Quantity);
    }

    [Fact]
    public async Task POST_orders_with_no_items_returns_400()
    {
        var request = ValidRequest() with { Items = [] };

        var response = await _client.PostAsJsonAsync("/orders", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_orders_with_a_duplicate_product_id_returns_400()
    {
        var request = ValidRequest() with
        {
            Items = [new CreateOrderItemRequest("sku-1", 1, 1m), new CreateOrderItemRequest("sku-1", 2, 1m)],
        };

        var response = await _client.PostAsJsonAsync("/orders", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_orders_with_a_non_positive_quantity_returns_400()
    {
        var request = ValidRequest() with { Items = [new CreateOrderItemRequest("sku-1", 0, 9.99m)] };

        var response = await _client.PostAsJsonAsync("/orders", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GET_orders_returns_a_previously_created_order()
    {
        var createResponse = await _client.PostAsJsonAsync("/orders", ValidRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();

        var response = await _client.GetAsync($"/orders/{created!.OrderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal(created.OrderId, body!.OrderId);
        Assert.Equal(OrderStatus.Created, body.Status);
    }

    [Fact]
    public async Task GET_orders_for_an_unknown_id_returns_404()
    {
        var response = await _client.GetAsync($"/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

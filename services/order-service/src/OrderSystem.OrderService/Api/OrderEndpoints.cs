using Microsoft.EntityFrameworkCore;
using OrderSystem.Contracts.Events;
using OrderSystem.Messaging;
using OrderSystem.OrderService.Domain;
using OrderSystem.OrderService.Persistence;

namespace OrderSystem.OrderService.Api;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", CreateOrderAsync);
        app.MapGet("/orders/{orderId:guid}", GetOrderAsync);

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        OrderDbContext db,
        IEventPublisher publisher,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var now = timeProvider.GetUtcNow();
        var orderId = Guid.NewGuid();
        var order = Order.Create(
            orderId,
            request.UserId,
            [.. request.Items.Select(i => new NewOrderItem(i.ProductId, i.Quantity, i.UnitPrice))],
            request.TotalAmount,
            request.ShippingAddress,
            request.PaymentMethod,
            now);

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        await publisher.PublishAsync(
            new OrderCreated(
                orderId,
                [.. request.Items.Select(i => new OrderItemPayload(i.ProductId, i.Quantity, i.UnitPrice))],
                request.TotalAmount,
                request.PaymentMethod),
            cancellationToken);

        return Results.Created($"/orders/{orderId}", OrderResponse.FromDomain(order));
    }

    private static async Task<IResult> GetOrderAsync(
        Guid orderId,
        OrderDbContext db,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        return order is null ? Results.NotFound() : Results.Ok(OrderResponse.FromDomain(order));
    }

    private static Dictionary<string, string[]> Validate(CreateOrderRequest request)
    {
        var errors = new Dictionary<string, List<string>>();

        void AddError(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list))
            {
                list = [];
                errors[key] = list;
            }

            list.Add(message);
        }

        if (request.Items.Count == 0)
        {
            AddError(nameof(request.Items), "An order must have at least one item.");
        }

        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            if (string.IsNullOrWhiteSpace(item.ProductId))
            {
                AddError($"{nameof(request.Items)}[{i}].{nameof(item.ProductId)}", "ProductId is required.");
            }

            if (item.Quantity <= 0)
            {
                AddError($"{nameof(request.Items)}[{i}].{nameof(item.Quantity)}", "Quantity must be greater than zero.");
            }

            if (item.UnitPrice < 0)
            {
                AddError($"{nameof(request.Items)}[{i}].{nameof(item.UnitPrice)}", "UnitPrice cannot be negative.");
            }
        }

        if (request.TotalAmount < 0)
        {
            AddError(nameof(request.TotalAmount), "TotalAmount cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(request.ShippingAddress))
        {
            AddError(nameof(request.ShippingAddress), "ShippingAddress is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            AddError(nameof(request.PaymentMethod), "PaymentMethod is required.");
        }

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }
}

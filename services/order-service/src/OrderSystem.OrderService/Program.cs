using OrderSystem.OrderService.Api;
using OrderSystem.OrderService.Messaging;
using OrderSystem.OrderService.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddOrderDbContext(
    builder.Configuration.GetConnectionString("OrderDb")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:OrderDb configuration."));

builder.Services.AddEventPublisher(builder.Configuration["ServiceBus:FullyQualifiedNamespace"]);
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapOrderEndpoints();

app.Run();

public partial class Program;

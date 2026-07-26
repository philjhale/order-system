using OrderSystem.OrderService.Api;
using OrderSystem.OrderService.Consumers;
using OrderSystem.OrderService.Messaging;
using OrderSystem.OrderService.DbMigration;
using OrderSystem.OrderService.Persistence;

// Run by the azurerm_container_app_job (task 10) instead of the normal web host — provisions
// this service's managed identity as a contained DB user, then applies EF Core migrations.
if (args.Length > 0 && args[0] == "migrate")
{
    await RunMigrationAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddOrderDbContext(
    builder.Configuration.GetConnectionString("OrderDb")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:OrderDb configuration."));

builder.Services.AddEventPublisher(builder.Configuration["ServiceBus:FullyQualifiedNamespace"]);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<OrderEventConsumer>();
builder.Services.AddHostedService<OrderEventConsumerHostedService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapOrderEndpoints();

app.Run();

static async Task RunMigrationAsync(string[] args)
{
    var hostBuilder = Host.CreateApplicationBuilder(args);
    hostBuilder.Services.AddOrderDbContext(
        hostBuilder.Configuration.GetConnectionString("OrderDb")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:OrderDb configuration."));
    hostBuilder.Services.Configure<SqlMigrationOptions>(hostBuilder.Configuration.GetSection("Sql"));
    hostBuilder.Services.AddSingleton<ISqlContainedUserProvisioner, SqlContainedUserProvisioner>();
    hostBuilder.Services.AddScoped<IOrderDbMigrator, OrderDbMigrator>();
    hostBuilder.Services.AddScoped<MigrationRunner>();

    using var host = hostBuilder.Build();
    await using var scope = host.Services.CreateAsyncScope();
    var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
    await runner.RunAsync(CancellationToken.None);
}

public partial class Program;

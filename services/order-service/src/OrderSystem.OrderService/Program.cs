using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderSystem.OrderService.Api;
using OrderSystem.OrderService.Consumers;
using OrderSystem.OrderService.HealthChecks;
using OrderSystem.OrderService.Messaging;
using OrderSystem.OrderService.DbMigration;
using OrderSystem.OrderService.Persistence;

// Run by the azurerm_container_app_job (services/order-service/infra/terraform) instead of the normal web host — provisions
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

builder.Services.AddOrderDbContext(GetOrderDbConnectionString(builder.Configuration));

builder.Services.AddEventPublisher(builder.Configuration["ServiceBus:FullyQualifiedNamespace"]);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<OrderEventConsumer>();
builder.Services.AddHostedService<OrderEventConsumerHostedService>();

// Backs the Container App's probes (services/order-service/infra/terraform). Split into two
// endpoints below rather than one shared /health: liveness/readiness poll continuously for the
// life of the replica and must stay cheap, while the DB-touching check only needs to run once
// per replica startup (Container Apps' startup_probe semantics) — Azure Container Apps caps
// periodic probe intervals at 4 minutes, far too frequent for Serverless SQL to ever accumulate
// the 60 idle minutes it needs to auto-pause, so the DB check can't be a recurring probe at all
// without keeping the database billed around the clock.
builder.Services.AddHealthChecks().AddCheck<OrderDbHealthCheck>("order-db");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapOrderEndpoints();

// Cheap process-alive check — no DB call — used by both liveness_probe and readiness_probe,
// which poll continuously for the life of the replica.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Round-trips to SQL (OrderDbHealthCheck), so a replica whose DB connection is broken at
// startup (Serverless auto-pause resume failure, missing contained user, etc.) never comes into
// rotation. Wired to the Container App's startup_probe only — which runs periodically until it
// succeeds once, then stops for the rest of the replica's life — not to liveness/readiness, so
// this endpoint doesn't keep touching (and billing) the database after startup.
app.MapHealthChecks("/health/startup");

app.Run();

static string GetOrderDbConnectionString(IConfiguration configuration) =>
    configuration.GetConnectionString("OrderDb")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:OrderDb configuration.");

static async Task RunMigrationAsync(string[] args)
{
    var hostBuilder = Host.CreateApplicationBuilder(args);
    hostBuilder.Services.AddOrderDbContext(GetOrderDbConnectionString(hostBuilder.Configuration));
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

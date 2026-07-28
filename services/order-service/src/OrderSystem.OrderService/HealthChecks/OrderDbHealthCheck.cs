using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderSystem.OrderService.Persistence;

namespace OrderSystem.OrderService.HealthChecks;

// Backs the Container App's liveness/readiness probes (services/order-service/infra/terraform) —
// a bare TCP probe would report healthy even if the SQL Serverless DB never resumed or the
// contained user was never provisioned, so this actually round-trips to the database.
public sealed class OrderDbHealthCheck(OrderDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Cannot connect to the order database.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cannot connect to the order database.", ex);
        }
    }
}

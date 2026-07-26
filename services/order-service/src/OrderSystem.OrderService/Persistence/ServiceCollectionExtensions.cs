using Microsoft.EntityFrameworkCore;

namespace OrderSystem.OrderService.Persistence;

public static class ServiceCollectionExtensions
{
    // EnableRetryOnFailure is required, not optional: Azure SQL Serverless
    // auto-pauses when idle, and the first connection after a resume can
    // take several seconds and needs to survive a transient connection
    // failure rather than crash the consumer/request.
    public static IServiceCollection AddOrderDbContext(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<OrderDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null)));

        return services;
    }
}

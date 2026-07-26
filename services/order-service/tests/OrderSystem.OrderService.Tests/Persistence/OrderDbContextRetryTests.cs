using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using OrderSystem.OrderService.Persistence;

namespace OrderSystem.OrderService.Tests.Persistence;

public class OrderDbContextRetryTests
{
    private static OrderDbContext BuildContext()
    {
        var services = new ServiceCollection();
        services.AddOrderDbContext("Server=test-server;Database=test-db;Trusted_Connection=True;");
        return services.BuildServiceProvider().GetRequiredService<OrderDbContext>();
    }

    [Fact]
    public void AddOrderDbContext_configures_the_SQL_Server_retrying_execution_strategy()
    {
        // Azure SQL Serverless auto-pauses when idle; the first connection
        // after a resume must survive a transient failure rather than throw.
        // This asserts EnableRetryOnFailure is actually wired, not just
        // present in source.
        using var context = BuildContext();

        var strategy = context.GetService<IExecutionStrategy>();

        Assert.IsType<SqlServerRetryingExecutionStrategy>(strategy);
    }

    // Constructing a real transient SqlException requires a live SQL Server
    // connection, so this exercises the same ExecutionStrategy retry loop
    // that SqlServerRetryingExecutionStrategy is built on, via a test-only
    // subclass that treats a simulated transient fault as retryable. It
    // proves a failing first attempt is retried and recovers rather than
    // throwing.
    [Fact]
    public void ExecutionStrategy_retries_a_transient_failure_and_recovers()
    {
        using var context = BuildContext();
        var dependencies = context.GetService<ExecutionStrategyDependencies>();
        var strategy = new TestRetryExecutionStrategy(dependencies);

        var attempts = 0;
        var result = strategy.Execute(() =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new TransientTestException();
            }

            return "recovered";
        });

        Assert.Equal("recovered", result);
        Assert.Equal(2, attempts);
    }

    private sealed class TransientTestException : Exception;

    private sealed class TestRetryExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(1))
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TransientTestException;
    }
}

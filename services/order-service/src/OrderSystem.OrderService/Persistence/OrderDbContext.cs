using Microsoft.EntityFrameworkCore;
using OrderSystem.OrderService.Domain;

namespace OrderSystem.OrderService.Persistence;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.HasKey(o => o.OrderId);
            order.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");

            order.HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            order.HasMany(o => o.OrderEvents)
                .WithOne()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            order.Metadata.FindNavigation(nameof(Order.Items))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
            order.Metadata.FindNavigation(nameof(Order.OrderEvents))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<OrderItem>(item =>
        {
            item.HasKey(i => new { i.OrderId, i.ProductId });
            item.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
            item.Property(i => i.Subtotal).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<OrderEvent>(orderEvent =>
        {
            orderEvent.HasKey(e => e.EventId);
            orderEvent.HasIndex(e => e.OrderId);
        });
    }
}

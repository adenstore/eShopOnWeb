using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.OrderService.Domain;

namespace Microsoft.eShopWeb.OrderService.Data;

/// <summary>
/// The Order service owns its own database. No other service reads or writes these tables;
/// they go through the HTTP API (or, later, messages on RabbitMQ).
/// </summary>
public class OrderDbContext : DbContext
{
    #pragma warning disable CS8618 // Required by Entity Framework
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}

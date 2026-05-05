using Microsoft.EntityFrameworkCore;

namespace Sample.EfCore;

public sealed class CatalogDbContext : DbContext
{
    public DbSet<Customer> Customers { get; } = new();

    public DbSet<Order> Orders { get; } = new();

    public IReadOnlyList<Order> ListOrdersInsideContext()
    {
        return Set<Order>().ToList();
    }
}

public sealed class Customer
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

public sealed class Order
{
    public int Id { get; init; }

    public Customer? Customer { get; init; }
}

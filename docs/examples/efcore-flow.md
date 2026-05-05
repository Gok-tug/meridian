# Example: EF Core Flow

EF Core support is available as a static preview for source `DbContext` types, `DbSet<TEntity>` containment, method-level `queries` edges, and direct method-level `writes` edges.

## Source pattern

```csharp
public sealed class OrdersDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class EfOrderRepository : IOrderRepository
{
    private readonly OrdersDbContext _context;

    public EfOrderRepository(OrdersDbContext context)
    {
        _context = context;
    }

    public Task<Order?> GetById(Guid id)
    {
        return _context.Orders.FirstOrDefaultAsync(order => order.Id == id);
    }

    public void Add(Order order)
    {
        _context.Orders.Add(order);
    }
}
```

## Expected graph

```text
EfOrderRepository
  --injects--> OrdersDbContext
  --queries--> Order
  --writes--> Order

OrdersDbContext
  --contains--> Order
```

## Path query

```bash
meridian path "GET /orders/{id}" "Order"
```

Expected output:

```text
GET /orders/{id}
  --sends--> GetOrderQuery
  --handled_by--> GetOrderQueryHandler
  --injects--> IOrderRepository
  --implemented_by--> EfOrderRepository
  --injects--> OrdersDbContext
  --queries--> Order
```

# Example: EF Core Flow

EF Core support is planned after the ASP.NET Core, DI, MediatR, and MCP milestones.

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
}
```

## Expected graph

```text
EfOrderRepository
  --injects--> OrdersDbContext
  --queries--> Order

OrdersDbContext
  --contains--> Orders
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

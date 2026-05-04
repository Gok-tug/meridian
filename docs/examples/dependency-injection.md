# Example: Dependency Injection

This example shows how Meridian should connect constructor dependencies to service registrations.

## Source pattern

```csharp
services.AddScoped<IOrderRepository, EfOrderRepository>();

public sealed class GetOrderQueryHandler
{
    private readonly IOrderRepository _orders;

    public GetOrderQueryHandler(IOrderRepository orders)
    {
        _orders = orders;
    }
}
```

## Expected graph

```text
GetOrderQueryHandler
  --injects--> IOrderRepository
  --implemented_by--> EfOrderRepository
```

Registration edge:

```text
IOrderRepository --registered_as--> EfOrderRepository
```

## Confidence

Direct generic registrations should be `EXTRACTED`:

```json
{
  "source": "type:MyApp.IOrderRepository",
  "target": "type:MyApp.EfOrderRepository",
  "relation": "registered_as",
  "confidence": "EXTRACTED",
  "confidence_score": 1.0
}
```

Assembly scanning should be `INFERRED` or `AMBIGUOUS` depending on how constrained the scan is.

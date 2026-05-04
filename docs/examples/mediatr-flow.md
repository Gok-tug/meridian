# Example: MediatR Flow

This example shows the core Meridian use case: connecting an HTTP endpoint to a MediatR request and handler.

## Source pattern

```csharp
app.MapGet("/orders/{id}", async (Guid id, ISender sender) =>
{
    return await sender.Send(new GetOrderQuery(id));
});

public sealed record GetOrderQuery(Guid Id) : IRequest<OrderDto>;

public sealed class GetOrderQueryHandler
    : IRequestHandler<GetOrderQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        // handler logic
    }
}
```

## Expected graph

```text
GET /orders/{id}
  --sends--> GetOrderQuery
  --handled_by--> GetOrderQueryHandler
```

## Expected edges

```json
[
  {
    "source": "endpoint:GET:/orders/{id}",
    "target": "type:MyApp.GetOrderQuery",
    "relation": "sends",
    "confidence": "EXTRACTED"
  },
  {
    "source": "type:MyApp.GetOrderQuery",
    "target": "type:MyApp.GetOrderQueryHandler",
    "relation": "handled_by",
    "confidence": "EXTRACTED"
  }
]
```

## CLI

```bash
meridian path "GET /orders/{id}" "GetOrderQueryHandler"
```

Expected output:

```text
GET /orders/{id}
  --sends--> GetOrderQuery
  --handled_by--> GetOrderQueryHandler
```

# Example: MediatR Flow

MediatR declaration analysis is available as a preview in `0.2.0-alpha.1`.

The current prototype connects source request and notification types to source handler types through generic MediatR interfaces. It does not yet detect `Send`, `Publish`, or endpoint-to-request flow.

## Source pattern

```csharp
public sealed record GetOrderQuery(Guid Id) : IRequest<OrderDto>;

public sealed class GetOrderQueryHandler
    : IRequestHandler<GetOrderQuery, OrderDto>
{
    public Task<OrderDto> Handle(GetOrderQuery request, CancellationToken cancellationToken) { ... }
}

public sealed record OrderUpdatedNotification(Guid Id) : INotification;

public sealed class OrderUpdatedNotificationHandler
    : INotificationHandler<OrderUpdatedNotification>
{
    public Task Handle(OrderUpdatedNotification notification, CancellationToken cancellationToken) { ... }
}
```

## Current graph

```text
GetOrderQuery
  --handled_by--> GetOrderQueryHandler

OrderUpdatedNotification
  --handled_by--> OrderUpdatedNotificationHandler
```

## Current edges

Conceptual edge shape; real node IDs include the assembly name and fully qualified symbol.

```json
[
  {
    "source": "type:MyApp.GetOrderQuery",
    "target": "type:MyApp.GetOrderQueryHandler",
    "relation": "handled_by",
    "confidence": "EXTRACTED"
  },
  {
    "source": "type:MyApp.OrderUpdatedNotification",
    "target": "type:MyApp.OrderUpdatedNotificationHandler",
    "relation": "handled_by",
    "confidence": "EXTRACTED"
  }
]
```

## CLI

```bash
meridian path "GetOrderQuery" "GetOrderQueryHandler"
```

Abridged expected output:

```text
GetOrderQuery
  --handled_by--> GetOrderQueryHandler
```

## Planned flow

A later analyzer should connect endpoint or application call sites to MediatR messages:

```csharp
app.MapGet("/orders/{id}", async (Guid id, ISender sender) =>
{
    return await sender.Send(new GetOrderQuery(id));
});
```

Planned graph:

```text
GET /orders/{id}
  --sends--> GetOrderQuery
  --handled_by--> GetOrderQueryHandler
```

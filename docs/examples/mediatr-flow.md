# Example: MediatR Flow

MediatR declaration analysis is available as a preview in `0.2.0-alpha.1`. Method-level `Send` and `Publish` call-site analysis is available as a preview in `0.2.0-alpha.2`.

The current prototype connects source request, stream request, and notification types to source handler types through generic MediatR interfaces. It also connects supported source methods that dispatch MediatR messages to the resolved request or notification type. Handled message nodes can come from generated or referenced code, though their graph nodes may not have source metadata.

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

public sealed class OrderDispatchSamples
{
    public async Task<OrderDto> DispatchInlineRequest(IMediator mediator, CancellationToken cancellationToken)
    {
        return await mediator.Send(new GetOrderQuery(Guid.NewGuid()), cancellationToken);
    }

    public async Task PublishInlineNotification(IPublisher publisher, CancellationToken cancellationToken)
    {
        await publisher.Publish(new OrderUpdatedNotification(Guid.NewGuid()), cancellationToken);
    }
}
```

## Current graph

```text
OrderDispatchSamples.DispatchInlineRequest
  --sends--> GetOrderQuery
  --handled_by--> GetOrderQueryHandler

OrderDispatchSamples.PublishInlineNotification
  --publishes--> OrderUpdatedNotification
  --handled_by--> OrderUpdatedNotificationHandler
```

## Current edges

Conceptual edge shape; real node IDs include the assembly name and fully qualified symbol.

```json
[
  {
    "source": "method:MyApp.OrderDispatchSamples.DispatchInlineRequest",
    "target": "type:MyApp.GetOrderQuery",
    "relation": "sends",
    "confidence": "EXTRACTED"
  },
  {
    "source": "type:MyApp.GetOrderQuery",
    "target": "type:MyApp.GetOrderQueryHandler",
    "relation": "handled_by",
    "confidence": "EXTRACTED"
  },
  {
    "source": "method:MyApp.OrderDispatchSamples.PublishInlineNotification",
    "target": "type:MyApp.OrderUpdatedNotification",
    "relation": "publishes",
    "confidence": "EXTRACTED"
  }
]
```

## CLI

```bash
meridian path "DispatchInlineRequest" "GetOrderQueryHandler"
```

Abridged expected output:

```text
OrderDispatchSamples.DispatchInlineRequest
  --sends--> GetOrderQuery
  --handled_by--> GetOrderQueryHandler
```

## Current limits

Supported call-site resolution is intentionally conservative:

- inline object creation,
- in-scope local object creation before the dispatch call,
- concrete request or notification method parameter static type fallback.

Not yet supported:

- ASP.NET Core endpoint-to-request bridging,
- MediatR `CreateStream`,
- interprocedural request tracking,
- runtime-created request or notification objects,
- direct method-to-handler shortcut edges.

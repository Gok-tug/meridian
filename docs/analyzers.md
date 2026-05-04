# Analyzers

Analyzer packs are responsible for turning Roslyn and framework evidence into Meridian graph facts.

Each analyzer should be small enough to test independently and should write only through the shared graph builder.

## Analyzer rules

Analyzers should:

- use Roslyn symbols where possible,
- add evidence for important facts,
- classify confidence consistently,
- avoid executing analyzed code,
- keep output deterministic,
- emit diagnostics when behavior is unsupported or ambiguous.

## Execution order

Framework-aware analysis should run in deterministic passes:

1. Roslyn foundation facts: symbols, locations, type/method nodes, `contains`, direct `calls`, and interface implementations.
2. Framework facts: DI registrations, constructor injection, endpoints, MediatR request/handler declarations, and later sends/publishes.
3. Cross-framework linking: combine facts into higher-level flow paths such as endpoint -> request -> handler -> injected service -> implementation.
4. Normalization and diagnostics: merge duplicates, sort deterministically, and report unsupported or ambiguous behavior.

DI facts are expected to be consumed by later ASP.NET Core and MediatR linking. Analyzer order should therefore be explicit instead of implied by source traversal or graph insertion order.

## Roslyn base analyzer

Implemented in the current prototype for project loading, type/method nodes, `contains`, and direct `calls` edges.

Responsibilities:

- load projects and compilations,
- index symbols,
- create type and method nodes,
- extract direct method calls,
- skip generated/bin/obj source noise by default,
- map symbols to source locations,
- provide shared semantic services to other analyzers.

Example relation:

```text
GetOrderQueryHandler.Handle --calls--> OrderService.GetByIdAsync
```

Confidence:

- `EXTRACTED` when Roslyn resolves the target symbol.
- `AMBIGUOUS` when overload or dynamic dispatch cannot be resolved precisely.

## ASP.NET Core analyzer

Planned for later `0.2.x` alpha work.

Responsibilities:

- discover MVC controllers,
- discover controller actions,
- read HTTP method attributes,
- discover Minimal API route mappings,
- create endpoint nodes,
- connect endpoints to action delegates or methods.

Supported patterns:

```csharp
[HttpGet("/orders/{id}")]
public Task<OrderDto> GetById(Guid id) { ... }
```

```csharp
app.MapGet("/orders/{id}", async (Guid id, ISender sender) =>
    await sender.Send(new GetOrderQuery(id)));
```

Relations:

```text
endpoint --calls--> action
endpoint --sends--> mediatr_request
endpoint --uses--> injected_service
```

## Dependency Injection analyzer

Initial prototype support exists for direct generic registrations, constructor injection, and source interface implementations. Convention-based registration and assembly scanning remain planned for later alpha versions.

Responsibilities:

- discover service registrations,
- map abstractions to implementations,
- discover constructor injection dependencies,
- connect consumers to required services,
- mark scanning-based registrations as inferred or ambiguous.

Supported direct registrations:

```csharp
services.AddScoped<IOrderRepository, EfOrderRepository>();
services.AddTransient<OrderService>();
services.AddSingleton<IClock, SystemClock>();
```

Relations:

```text
IOrderRepository --registered_as--> EfOrderRepository
GetOrderQueryHandler --injects--> IOrderRepository
IOrderRepository --implemented_by--> EfOrderRepository
```

Confidence:

- `EXTRACTED` for direct generic registrations.
- `INFERRED` for convention-based registration.
- `AMBIGUOUS` for assembly scanning with multiple candidates.

## MediatR analyzer

Initial declaration support exists in `0.2.0-alpha.1`. `Send` and `Publish` call-site detection remains planned.

Current responsibilities:

- discover source request, stream request, and notification types,
- discover source handlers,
- connect request, stream request, and notification types to handlers,
- keep `handled_by` edges when a source handler handles a message type from generated or referenced code without an analyzable source location.

Planned responsibilities:

- detect `Send` and `Publish` calls,
- support `IMediator`, `ISender`, and `IPublisher` call sites.

Supported types:

```csharp
public sealed record GetOrderQuery(Guid Id) : IRequest<OrderDto>;

public sealed record WatchOrders(Guid Id) : IStreamRequest<OrderDto>;

public sealed class GetOrderQueryHandler
    : IRequestHandler<GetOrderQuery, OrderDto>
{
    public Task<OrderDto> Handle(GetOrderQuery request, CancellationToken cancellationToken) { ... }
}

public sealed class WatchOrdersHandler
    : IStreamRequestHandler<WatchOrders, OrderDto>
{
    public IAsyncEnumerable<OrderDto> Handle(WatchOrders request, CancellationToken cancellationToken) { ... }
}
```

Current relations:

```text
GetOrderQuery --handled_by--> GetOrderQueryHandler
OrderCreatedNotification --handled_by--> OrderCreatedNotificationHandler
```

Planned relations:

```text
endpoint --sends--> GetOrderQuery
publisher --publishes--> OrderCreatedNotification
```

Confidence:

- `EXTRACTED` when generic interface arguments identify the handler relationship.
- Planned: `EXTRACTED` when `Send(new Request(...))` resolves to a request type.
- Planned: `INFERRED` when request type is inferred through a variable with strong symbol evidence.
- Planned: `AMBIGUOUS` when runtime type construction prevents a single target.

## EF Core analyzer

Planned for `0.4.0-alpha.1`.

Responsibilities:

- discover `DbContext` types,
- discover `DbSet<TEntity>` properties,
- connect service/handler methods to DbContext usage,
- connect DbContext access to entity types,
- detect `_context.Set<TEntity>()`.

Relations:

```text
GetOrderQueryHandler --uses--> OrderDbContext
OrderDbContext --contains--> Orders
GetOrderQueryHandler --queries--> Order
```

## Reflection analyzer

Planned for `0.4.0-alpha.1`.

Responsibilities:

- detect reflection sites,
- detect assembly loading,
- detect type references through `typeof` and `nameof`,
- detect `Activator.CreateInstance`,
- classify dynamic behavior with confidence.

Supported patterns:

```csharp
typeof(OrderService)
Assembly.GetExecutingAssembly().GetTypes()
Activator.CreateInstance(type)
```

Confidence:

- `EXTRACTED` for `typeof(SomeType)`.
- `INFERRED` for constrained assembly scanning.
- `AMBIGUOUS` for runtime strings or multiple possible target types.

## Rust/native interop analyzer

Future scope.

Initial support should detect .NET boundaries that call native or Rust-backed libraries.

Supported patterns to investigate:

```csharp
[DllImport("orders_native")]
private static extern int calculate_order_total(...);

[LibraryImport("orders_native")]
private static partial int calculate_order_total(...);
```

Relations:

```text
OrderPricingService --crosses_boundary--> orders_native
orders_native --loads--> native_library
```

This is not full Rust static analysis. It is .NET-side native interop detection.

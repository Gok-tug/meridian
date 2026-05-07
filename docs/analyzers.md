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

1. Roslyn foundation facts: symbols, locations, type/method/member nodes, `contains`, direct `calls`, member `reads`/`writes`/`uses`, conditional-flow preview facts, source-generator preview facts, typed Avalonia AXAML binding preview facts, and interface implementations.
2. Framework facts: DI registrations, constructor injection, endpoints, MediatR request/handler declarations, and MediatR sends/publishes.
3. Cross-framework linking: combine facts into higher-level flow paths such as endpoint -> request -> handler -> injected service -> implementation.
4. Normalization and diagnostics: merge duplicates, sort deterministically, and report unsupported or ambiguous behavior.

DI facts are expected to be consumed by later ASP.NET Core and MediatR linking. Analyzer order should therefore be explicit instead of implied by source traversal or graph insertion order.

## Roslyn base analyzer

Implemented for project loading, type/method/member nodes, `contains`, direct `calls`, and conservative member-reference edges.

Responsibilities:

- load projects and compilations,
- index symbols,
- create type, method, enum, enum member, property, and field nodes,
- extract direct method calls,
- extract ordinary-method static references to source properties, fields, enum types, and enum members,
- classify direct property/field access in ordinary methods as `reads`, `writes`, or both for assignments, compound assignments, increments, `ref`, and `out`,
- emit `uses` for enum references and `nameof(...)` references,
- emit method-level `branches_on` and `switches_on` preview edges for directly resolved symbols in simple `if` and `switch` conditions,
- synthesize narrow CommunityToolkit.Mvvm generated-member preview facts from `[ObservableProperty]` and `[RelayCommand]` source attributes,
- resolve conservative typed Avalonia AXAML binding preview facts after C# symbol analysis,
- skip generated/bin/obj source noise by default,
- map symbols to source locations,
- provide shared semantic services to other analyzers.

Example relations:

```text
GetOrderQueryHandler.Handle --calls--> OrderService.GetByIdAsync
TaskExecutor.Execute --reads--> MintTask.ExecutionMode
TaskExecutor.ChangeMode --writes--> MintTask.ExecutionMode
TaskExecutor.Execute --uses--> ExecutionMode.RuntimeSigning
RoutingDecider.Decide --branches_on--> RoutingMode.Fast
RoutingDecider.Decide --switches_on--> RoutingMode.Offline
RecordingViewModel.ToggleRecordingCommand --generated_from--> RecordingViewModel.ToggleRecording
MainWindow --binds_to--> RecordingViewModel.ToggleRecordingCommand
```

Confidence:

- `EXTRACTED` when Roslyn resolves the target symbol.
- `AMBIGUOUS` when overload or dynamic dispatch cannot be resolved precisely.

Member references and conditional-flow facts are intentionally limited to ordinary methods and conservative. Meridian does not extract constructor, accessor, or operator member references, and conditional edges do not prove branch reachability, path sensitivity, interprocedural dataflow, runtime dispatch resolution, or UI binding behavior.

## Conditional flow analyzer

Preview support is implemented for simple method-body conditions.

Responsibilities:

- inspect ordinary method `if` conditions,
- inspect ordinary method `switch` governing expressions and constant case labels,
- connect the enclosing method to source-resolved properties, fields, enum types, enum members, and simple constants with `branches_on` or `switches_on`,
- preserve condition or case text in edge metadata.

Unsupported patterns are skipped rather than guessed. Conditional edges are static source facts; they do not prove runtime reachability or path selection.

## CommunityToolkit.Mvvm analyzer

Preview support is implemented for source attributes that imply generated members.

Responsibilities:

- detect `[ObservableProperty]` fields from `CommunityToolkit.Mvvm.ComponentModel`,
- synthesize generated public `property` nodes using standard Toolkit naming,
- detect `[RelayCommand]` methods from `CommunityToolkit.Mvvm.Input`,
- synthesize `mvvm_command` nodes with async command metadata when the source method is async,
- connect generated nodes back to source fields or methods with `generated_from`.

The analyzer does not include generated `.g.cs` files, execute source generators, or model command runtime behavior.

## Avalonia AXAML binding analyzer

Preview support is implemented for static AXAML binding facts when a binding scope is typed.

Responsibilities:

- discover project-local `.axaml` files while reusing generated/bin/obj source filters,
- resolve `x:Class`, `x:DataType`, `DataTemplate x:DataType`, and `DataTemplate DataType="{x:Type ...}"` through XML namespace aliases,
- parse simple `{Binding Property}` / `{Binding Path=Property}` and `{CompiledBinding Property}` expressions,
- resolve the first path segment to source properties, generated `[ObservableProperty]` properties, and `[RelayCommand]` commands,
- connect typed views to resolved properties, commands, and static view-model/data-template scopes with `binds_to`,
- emit diagnostics for unscoped, unresolved, parse-failed, or unsupported binding shapes, including `MERIDIAN_AXAML_DIAGNOSTICS_TRUNCATED` when unique per-file diagnostics exceed the graph cap.

The analyzer does not execute Avalonia, infer runtime `DataContext`, evaluate ViewLocator conventions, resolve converters, or model `MultiBinding`, `RelativeSource`, `ElementName`, `Source`, untyped `$parent[...]`, indexers, or arbitrary control-tree paths. Per-file diagnostic caps keep pathological AXAML output bounded and add an explicit truncation diagnostic when extra unique diagnostics are omitted.

## ASP.NET Core analyzer

Preview support is implemented for source-resolved endpoint entrypoints.

Responsibilities:

- discover MVC controller route/action attributes,
- replace common `[controller]` and `[action]` route tokens,
- discover Minimal API route mappings for `MapGet`, `MapPost`, `MapPut`, `MapDelete`, and `MapPatch`,
- track simple local `MapGroup` prefixes in the same block before route mapping calls,
- discover FastEndpoints `Get(...)` and `Post(...)` calls in `Configure()`,
- discover MinimalApi.Endpoint-style `AddRoute` methods that map routes and delegate to source `HandleAsync`,
- create synthetic endpoint nodes,
- connect endpoints to source action, method group, `ExecuteAsync`, or `HandleAsync` methods,
- connect inline endpoint lambdas directly to mediator messages when the lambda performs a supported `Send` or `Publish` call.

Supported patterns:

```csharp
[Route("[controller]/[action]")]
public sealed class OrdersController : Controller
{
    [HttpGet("{id}")]
    public Task<IActionResult> Detail(Guid id) { ... }
}
```

```csharp
app.MapPost("/orders", CreateOrder);
var api = app.MapGroup("/api");
api.MapGet("/orders/{id}", GetOrder);
```

```csharp
public override void Configure()
{
    Post(CreateOrderRequest.Route);
}
```

Relations:

```text
endpoint --calls--> action_or_handler
endpoint --sends--> mediatr_request
endpoint --publishes--> mediatr_notification
```

## Dependency Injection analyzer

Preview support is implemented for direct generic registrations, narrow direct-`new` factory registrations, direct `GetRequiredService<TImplementation>()` factory aliases, constructor injection, and source interface implementations. Convention-based registration and assembly scanning remain planned.

Responsibilities:

- discover service registrations,
- map abstractions to implementations,
- discover constructor injection dependencies,
- connect consumers to required services,
- mark scanning-based registrations as inferred or ambiguous.

Supported registrations:

```csharp
services.AddScoped<IOrderRepository, EfOrderRepository>();
services.AddTransient<OrderService>();
services.AddSingleton<IClock, SystemClock>();
services.AddSingleton<IClock>(_ => new SystemClock());
services.AddSingleton<ClockFactory>(sp =>
{
    var createdAt = DateTimeOffset.UtcNow;
    return new ClockFactory(createdAt);
});
services.AddSingleton<INotificationSender>(sp => sp.GetRequiredService<EmailNotificationSender>());
```

Factory support is intentionally narrow: expression-bodied lambdas must directly create the implementation or directly return `GetRequiredService<TImplementation>()`, and block-bodied lambdas must end with one top-level direct `return new Implementation(...);` or `return sp.GetRequiredService<TImplementation>();` statement. Earlier top-level local declarations or expression statements are allowed; branching, nested delegate invocation, `GetService<T>()`, `Func<T>` factories, and runtime resolver dictionaries are skipped.

Relations:

```text
IOrderRepository --registered_as--> EfOrderRepository
GetOrderQueryHandler --injects--> IOrderRepository
IOrderRepository --implemented_by--> EfOrderRepository
```

Confidence:

- `EXTRACTED` for direct generic registrations, direct object creation factory registrations, and direct `GetRequiredService<TImplementation>()` factory aliases.
- `INFERRED` for convention-based registration.
- `AMBIGUOUS` for assembly scanning with multiple candidates.

## MediatR analyzer

Preview support covers declarations, method-level `Send` and `Publish` call sites, and `Mediator.SourceGenerator`-style `Mediator` namespace interfaces.

Implemented responsibilities:

- discover source request, command, query, stream request, and notification types,
- discover source handlers,
- connect request, command, query, stream request, and notification types to handlers,
- keep `handled_by` edges when a source handler handles a message type from generated or referenced code without an analyzable source location,
- detect supported `Send` and `Publish` calls on `IMediator`, `ISender`, and `IPublisher` in either `MediatR` or `Mediator` namespaces,
- connect enclosing source methods to resolved request or notification types.

Planned extensions:

- broader endpoint delegate and mediator dataflow beyond direct source-resolved cases,
- `CreateStream` call-site detection,
- interprocedural request tracking,
- diagnostics for ambiguous runtime-created messages.

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

Supported call-site patterns:

```csharp
await mediator.Send(new GetOrderQuery(id), cancellationToken);

var command = new RefreshCacheCommand();
await sender.Send(command, cancellationToken);

await publisher.Publish(new OrderCreatedNotification(id), cancellationToken);
```

Current relations:

```text
OrderController.Get --sends--> GetOrderQuery
GetOrderQuery --handled_by--> GetOrderQueryHandler
OrderService.Save --publishes--> OrderCreatedNotification
OrderCreatedNotification --handled_by--> OrderCreatedNotificationHandler
```

Confidence:

- `EXTRACTED` when generic interface arguments identify the handler relationship.
- `EXTRACTED` when inline object creation or in-scope local object creation resolves to a request or notification type.
- `INFERRED` when a concrete method parameter type identifies the dispatched request or notification.
- `AMBIGUOUS` is reserved for future diagnostics when runtime type construction prevents a single target.

## EF Core analyzer

Current static preview.

Responsibilities:

- discover source `DbContext` types,
- discover `DbSet<TEntity>` properties,
- connect service/handler methods to DbContext usage,
- connect read operations to entity types with `queries`,
- connect direct mutation operations to entity types with `writes`,
- detect `_context.Set<TEntity>()`.

Relations:

```text
GetOrderQueryHandler --uses--> OrderDbContext
OrderDbContext --contains--> Order
GetOrderQueryHandler --queries--> Order
CreateOrderHandler --writes--> Order
```

## Reflection analyzer

Current static preview.

Responsibilities:

- detect statically resolved type references through `typeof`,
- detect statically resolved `Activator.CreateInstance` targets,
- emit diagnostics for runtime-only reflection targets instead of guessed edges.

Supported patterns:

```csharp
typeof(OrderService)
Activator.CreateInstance<OrderService>()
Activator.CreateInstance(typeof(OrderService))
```

Confidence:

- `EXTRACTED` for statically resolved reflection targets.
- Diagnostics for runtime-only targets such as `Activator.CreateInstance(type)`.
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

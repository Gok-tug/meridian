# Limitations

Meridian should be explicit about what it cannot know statically.

Clear limitations make the tool more trustworthy because users can understand when an edge is a fact, an inference, or an ambiguous possibility.

## Static analysis limits

Meridian analyzes source code and project metadata. It does not run the application.

That means it may not fully resolve:

- runtime configuration,
- environment-specific service registration,
- dynamically loaded assemblies,
- reflection from runtime strings,
- polymorphic dispatch selected by runtime state,
- conditional compilation not active in the loaded build configuration.

## Roslyn/MSBuild limits

Meridian depends on successful project loading.

Analysis can be incomplete when:

- the solution does not restore,
- SDKs are missing,
- custom MSBuild targets fail,
- needed generated code is filtered or unavailable,
- projects require environment-specific properties,
- the loaded target framework differs from the production target.

## Graph summary limits

`agent-summary` and MCP summary tools are derived views over the loaded `graph.json`. They do not reread source code, prove architectural ownership, or prove source-code absence.

Central-node and extension-point rankings are deterministic heuristics based on graph structure, node names, node kinds, and known relation types. They are navigation hints for agents, not a replacement for source review.

Graph clusters use non-containment graph components only when the loaded graph has enough separated structure. A cluster means "these nodes are connected in the graph," not "this is a real subsystem boundary." Summary rankings and clusters score distinct structural non-containment edges by source, target, and relation; raw graph JSON and graph statistics still preserve all evidence-bearing edges. Budget modes cap returned items deterministically; they are not exact token counts.

## Member graph limits

Member graph analysis is conservative. Meridian emits source enum, enum member, property, and field declaration nodes, plus ordinary-method references when Roslyn directly resolves the symbol.

It does not perform:

- member-reference extraction from constructors, property/event accessors, operators, or conversion operators,
- full interprocedural dataflow,
- runtime dynamic dispatch resolution,
- path-sensitive control-flow or branch reachability analysis,
- XAML/View-ViewModel binding analysis,
- arbitrary reflection or string-based member resolution.

`reads` and member-level `writes` edges describe direct source member access in an ordinary method body. Entity-level EF Core `writes` edges describe direct static DbSet/DbContext mutation calls when the entity type is known. Neither form proves a runtime path always reads or writes that target.

## Dependency Injection limits

Source-resolved direct generic registrations, narrow direct-`new` factory registrations, and direct `GetRequiredService<TImplementation>()` factory aliases are the currently reliable DI cases:

```csharp
services.AddScoped<IOrderRepository, EfOrderRepository>();
services.AddSingleton<IClock>(_ => new SystemClock());
services.AddSingleton<ClockFactory>(_ =>
{
    var createdAt = DateTimeOffset.UtcNow;
    return new ClockFactory(createdAt);
});
services.AddSingleton<INotificationSender>(sp => sp.GetRequiredService<EmailNotificationSender>());
```

Complex factory or convention-based registrations may be inferred, ambiguous, or skipped:

```csharp
services.AddScoped<IOrderRepository>(sp => CreateRepository(sp));
services.AddScoped<IOrderRepository>(sp => sp.GetRequiredService<Func<IOrderRepository>>()());
services.AddScoped<IOrderRepository>(sp => serviceResolver[tenant]());
services.AddScoped<IOrderRepository>(sp =>
{
    if (UseSql())
    {
        return new SqlOrderRepository();
    }

    return new EfOrderRepository();
});
services.Scan(scan => scan.FromAssemblies(...));
```

The current prototype only emits constructor injection edges for unambiguous source class constructors. Multiple unmarked constructors and record constructors are not treated as extracted DI facts yet.

Meridian should not claim exact DI behavior when registration depends on runtime assembly contents, `Func<T>` factories, nested delegate invocation, `GetService<T>()`, factory `.Create()` calls, resolver dictionaries, complex factory delegates, ambiguous constructor selection, or external configuration.

## ASP.NET Core endpoint limits

ASP.NET Core endpoint analysis is a source preview. Meridian emits endpoint nodes for MVC route attributes, Minimal API `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch` calls, simple local `MapGroup` prefixes, FastEndpoints route verbs in `Configure()`, and MinimalApi.Endpoint-style route-registration methods when the source pattern is statically visible.

It does not model:

- ASP.NET Core route precedence,
- authorization, filters, middleware, or endpoint conventions,
- model binding behavior,
- runtime route discovery,
- arbitrary delegate dataflow,
- routes built from runtime-only strings,
- generated endpoint code execution.

Known routes with unresolved handlers produce warnings instead of guessed handler edges.

## MediatR limits

The current prototype emits declaration facts for source-resolved MediatR or `Mediator` namespace requests, commands, queries, stream requests, notifications, and handlers. Generic handler relationships are reliable when the handler is available in analyzable source; handled message nodes may omit source metadata when the message type comes from generated or referenced code.

The current prototype also emits method-level `sends` and `publishes` edges for supported `IMediator`, `ISender`, and `IPublisher` call sites in either namespace. Supported message resolution is intentionally conservative: inline object creation, in-scope local object creation before the dispatch call, and concrete method parameter static type fallback.

Runtime-created requests remain ambiguous:

```csharp
object request = CreateRequestFromRuntimeData();
await mediator.Send(request);
```

Meridian should not claim `CreateStream`, interprocedural request tracking, runtime-created message resolution, direct method-to-handler shortcut edges, or runtime handler discovery until those analyzers exist.

## Reflection limits

Reflection analysis is a static preview. Meridian emits `reflects` edges for statically resolved `typeof(T)`, `Activator.CreateInstance<T>()`, and `Activator.CreateInstance(typeof(T))` targets, and emits diagnostics instead of guessed edges for runtime-only targets.

Reliable:

```csharp
typeof(OrderService)
Activator.CreateInstance<OrderService>()
Activator.CreateInstance(typeof(OrderService))
```

Ambiguous:

```csharp
Type.GetType(typeNameFromConfig)
Activator.CreateInstance(type)
```

## EF Core limits

EF Core analysis is a static preview. Meridian detects source `DbContext` types, `DbSet<TEntity>` containment, DbContext usage, method-level `queries` edges for read operations, and method-level `writes` edges for direct DbSet/DbContext mutation calls when the entity type is statically known.

It may not know:

- final SQL,
- provider-specific behavior,
- runtime query filters,
- database schema drift,
- migrations not present in source,
- dynamic entity access through runtime types,
- entity-specific effects of `SaveChanges` when the mutated entities are not visible from direct source mutation calls.

## Source generator limits

Source-generator-heavy projects require special care.

The current prototype filters generated source by default to reduce graph noise and keep golden output stable. Default filters include `obj`, `bin`, `*.g.cs`, `*.generated.cs`, and `*.designer.cs`.

Generated-only members or types may be omitted until Meridian has explicit source-generator support or an include-generated option. CommunityToolkit.Mvvm generated members are a known example: `[RelayCommand]` methods may appear as source methods, but generated `*Command` properties are not emitted yet; `[ObservableProperty]` generated public properties are likewise not graph facts unless visible in analyzable source.

## Native/Rust interop limits

Future Rust/native support starts at the .NET boundary.

Meridian does not yet model `DllImport`, `LibraryImport`, or `NativeLibrary.Load/TryLoad` as first-class graph facts. Future support may detect that .NET calls a native library, but it should not initially claim to analyze Rust, C, or C++ internals or produce a native implementation call graph.

## Performance limits

Large solutions can be slow because Roslyn workspace loading is expensive.

Meridian should report phase timings so users can see whether time is spent in workspace loading, compilation, analyzers, graph construction, or export.

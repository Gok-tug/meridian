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

## Dependency Injection limits

Source-resolved direct generic registrations are the currently reliable DI case:

```csharp
services.AddScoped<IOrderRepository, EfOrderRepository>();
```

Runtime, factory, or convention-based registrations may be inferred, ambiguous, or skipped:

```csharp
services.AddScoped<IOrderRepository>(_ => new EfOrderRepository());
services.Scan(scan => scan.FromAssemblies(...));
```

The current prototype only emits constructor injection edges for unambiguous source class constructors. Multiple unmarked constructors and record constructors are not treated as extracted DI facts yet.

Meridian should not claim exact DI behavior when registration depends on runtime assembly contents, factory delegates, ambiguous constructor selection, or external configuration.

## MediatR limits

The current prototype emits declaration facts for source-resolved MediatR requests, notifications, and handlers. Generic request/handler relationships are reliable when both the handler and handled message type are available in analyzable source.

MediatR call-site flow is not implemented yet. Runtime-created requests remain ambiguous:

```csharp
object request = CreateRequestFromRuntimeData();
await mediator.Send(request);
```

Meridian should not claim `Send`, `Publish`, endpoint-to-request flow, or runtime handler discovery until those analyzers exist.

## Reflection limits

Reflection analysis is planned, not part of the current prototype. Reflection is often not fully statically resolvable.

Reliable:

```csharp
typeof(OrderService)
```

Ambiguous:

```csharp
Type.GetType(typeNameFromConfig)
Activator.CreateInstance(type)
```

## EF Core limits

EF Core analysis is planned, not part of the current prototype. The planned analyzer should detect DbContext and DbSet usage from source symbols.

It may not know:

- final SQL,
- provider-specific behavior,
- runtime query filters,
- database schema drift,
- migrations not present in source,
- dynamic entity access through runtime types.

## Source generator limits

Source-generator-heavy projects require special care.

The current prototype filters generated source by default to reduce graph noise and keep golden output stable. Default filters include `obj`, `bin`, `*.g.cs`, `*.generated.cs`, and `*.designer.cs`.

Generated-only members or types may be omitted until Meridian has explicit source-generator support or an include-generated option.

## Native/Rust interop limits

Future Rust/native support starts at the .NET boundary.

Meridian may detect that .NET calls a native library, but it should not initially claim to analyze Rust internals or produce a Rust call graph.

## Performance limits

Large solutions can be slow because Roslyn workspace loading is expensive.

Meridian should report phase timings so users can see whether time is spent in workspace loading, compilation, analyzers, graph construction, or export.

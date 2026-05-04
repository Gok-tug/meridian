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
- generated code is unavailable,
- projects require environment-specific properties,
- the loaded target framework differs from the production target.

## Dependency Injection limits

Direct registrations are reliable:

```csharp
services.AddScoped<IOrderRepository, EfOrderRepository>();
```

Runtime or convention-based registrations may be inferred or ambiguous:

```csharp
services.Scan(scan => scan.FromAssemblies(...));
```

Meridian should not claim exact DI behavior when registration depends on runtime assembly contents or external configuration.

## MediatR limits

Generic request/handler relationships are reliable when source is available.

Runtime-created requests may be ambiguous:

```csharp
object request = CreateRequestFromRuntimeData();
await mediator.Send(request);
```

## Reflection limits

Reflection is often not fully statically resolvable.

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

Meridian can detect DbContext and DbSet usage from source symbols.

It may not know:

- final SQL,
- provider-specific behavior,
- runtime query filters,
- database schema drift,
- migrations not present in source,
- dynamic entity access through runtime types.

## Source generator limits

Source-generator-heavy projects require special care.

Meridian may initially analyze only source available through Roslyn after project load. Generator output support should be documented per release.

## Native/Rust interop limits

Future Rust/native support starts at the .NET boundary.

Meridian may detect that .NET calls a native library, but it should not initially claim to analyze Rust internals or produce a Rust call graph.

## Performance limits

Large solutions can be slow because Roslyn workspace loading is expensive.

Meridian should report phase timings so users can see whether time is spent in workspace loading, compilation, analyzers, graph construction, or export.

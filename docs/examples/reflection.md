# Example: Reflection and Assembly Scanning

Reflection support should be confidence-aware because many reflection patterns are not fully statically resolvable.

## Direct type reference

```csharp
var type = typeof(OrderService);
```

Expected relation:

```text
reflection_site --reflects--> OrderService [EXTRACTED]
```

## Assembly scanning

```csharp
services.Scan(scan => scan
    .FromAssembliesOf(typeof(IOrderRepository))
    .AddClasses(classes => classes.AssignableTo<IOrderRepository>())
    .AsImplementedInterfaces()
    .WithScopedLifetime());
```

Expected relation:

```text
assembly_scan --scans--> IOrderRepository [INFERRED]
IOrderRepository --implemented_by--> EfOrderRepository [INFERRED]
```

If multiple implementations exist, Meridian should emit ambiguous candidates:

```text
IOrderRepository --implemented_by--> EfOrderRepository [AMBIGUOUS]
IOrderRepository --implemented_by--> CachedOrderRepository [AMBIGUOUS]
```

## Runtime type names

```csharp
var type = Type.GetType(configuration["HandlerType"]);
var instance = Activator.CreateInstance(type);
```

Expected behavior:

- create a reflection site node,
- mark possible target as `AMBIGUOUS` if any target can be inferred,
- emit a diagnostic if no static target can be found.

# Example: Reflection and Assembly Scanning

Reflection support is a static preview because many reflection patterns are not fully statically resolvable.

## Direct type reference

```csharp
var type = typeof(OrderService);
```

Expected relation:

```text
SomeMethod --reflects--> OrderService [EXTRACTED]
```

## Assembly scanning

Assembly scanning is still planned and is not part of the current static reflection preview.

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

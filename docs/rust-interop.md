# Rust and Native Interop

Rust support is planned, but the initial Meridian product is focused on .NET application-flow analysis.

The correct first scope is Rust/native interop boundary detection from the .NET side, not full Rust static analysis.

## Position

Meridian should say:

```text
Rust interop detection is planned for future versions, focused on .NET applications that call Rust/native components through FFI boundaries.
```

This gives the community a clear signal without overpromising full Rust analysis.

## Why not full Rust in the MVP

Full Rust static analysis would require a different semantic engine and project model.

Meridian's initial value comes from Roslyn and .NET framework analysis:

- ASP.NET Core,
- dependency injection,
- MediatR,
- EF Core,
- reflection,
- MSBuild solution context.

Adding full Rust too early would dilute the MVP and delay the .NET value proposition.

## Planned first Rust/native scope

Detect .NET code that crosses into native or Rust-backed libraries.

Patterns:

```csharp
[DllImport("orders_native")]
private static extern int calculate_order_total(...);
```

```csharp
[LibraryImport("orders_native")]
private static partial int calculate_order_total(...);
```

Potential graph:

```text
OrderPricingService
  --calls--> NativePricing.calculate_order_total
  --crosses_boundary--> orders_native
```

Potential node kinds:

```text
native_library
ffi_boundary
native_method
```

Potential relations:

```text
crosses_boundary
loads
calls
```

## What Meridian should not claim initially

Meridian should not initially claim to:

- parse Rust projects,
- understand Cargo workspaces,
- build Rust call graphs,
- resolve Rust traits or macros,
- connect native method internals back into .NET flow.

Those can remain future research items.

## Future research

Possible later directions:

- Cargo project detection,
- Rust symbol extraction,
- native export mapping,
- C ABI binding correlation,
- generated binding detection,
- graph stitching between .NET and Rust graphs.

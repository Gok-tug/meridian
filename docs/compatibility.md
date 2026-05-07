# Compatibility

Meridian compatibility must be documented honestly because Roslyn and MSBuild behavior depends on SDK, project type, and language version.

## Runtime target

The package currently targets `net10.0`.

Before a stable release, Meridian should decide whether the distributed global tool targets `net8.0`, `net10.0`, or another supported runtime based on Roslyn/MSBuildWorkspace package compatibility and user reach.

## Compatibility matrix

| Area | Target |
| --- | --- |
| .NET SDK | current supported SDK used by the tool |
| C# language versions | compiler-supported versions loaded by Roslyn |
| Project system | SDK-style projects first |
| Build system | MSBuildWorkspace |
| Operating systems | Windows, Linux, macOS |
| Package distribution | NuGet global tool |

## Initial project loading targets

Expected to load first, even when framework-specific analyzers are still planned:

- class libraries,
- ASP.NET Core Web API projects,
- ASP.NET Core Minimal API projects,
- Worker Service projects where Roslyn loading succeeds.

Later:

- older non-SDK-style projects,
- complex multi-targeted solutions,
- source-generator-heavy projects,
- custom MSBuild imports.

## Framework support

| Framework/pattern | Status |
| --- | --- |
| Direct C# calls | Implemented |
| Microsoft.Extensions.DependencyInjection | Preview for direct generic registrations, narrow direct-`new` factory registrations, direct `GetRequiredService<TImplementation>()` factory aliases, constructor injection, and source interface implementations; broader coverage planned |
| ASP.NET Core MVC | Preview for controller route/action attributes, common route tokens, and endpoint-to-action `calls` edges |
| ASP.NET Core Minimal APIs | Preview for `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch`, simple local `MapGroup` prefixes, and endpoint-to-handler or direct mediator edges |
| FastEndpoints / MinimalApi.Endpoint | Preview for `Configure()` HTTP verbs and `AddRoute` methods that delegate to source handlers |
| MediatR / Mediator | Preview for source request/command/query/notification/handler declarations, `handled_by`, and method-level `Send`/`Publish` call-site flow |
| MCP server | Preview over generated `graph.json` files; no live Roslyn scan during MCP calls; supports `reload_graph` for refreshing the configured graph file in a running server |
| EF Core | Static preview for source `DbContext`, `DbSet<TEntity>`, `_context.Entities`, `_context.Set<TEntity>()`, method-level `queries` edges, and direct method-level `writes` edges |
| Reflection/assembly scanning | Static reflection preview for `typeof(T)` and `Activator.CreateInstance` targets; assembly scanning remains planned |
| Rust/native interop boundaries | Planned |

## Unsupported until documented

Unless tests and docs say otherwise, assume the following are unsupported or best-effort:

- runtime-generated registrations,
- dynamic code loading,
- non-SDK-style projects,
- custom build systems outside MSBuildWorkspace,
- full source generator output analysis,
- full Rust static analysis,
- runtime profiling,
- binary-only dependency analysis.

## Compatibility policy

Each release should document:

- tested .NET SDK version,
- tested operating systems,
- supported analyzer packs,
- graph schema version,
- known limitations.

If a release changes graph schema or CLI machine-readable output, it must call that out in `CHANGELOG.md`.

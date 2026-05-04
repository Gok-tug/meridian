# Compatibility

Meridian compatibility must be documented honestly because Roslyn and MSBuild behavior depends on SDK, project type, and language version.

## Current prototype

The current package prototype targets `net10.0`.

Before a stable release, Meridian should decide whether the distributed global tool targets `net8.0`, `net10.0`, or another supported runtime based on Roslyn/MSBuildWorkspace package compatibility and user reach.

## Planned compatibility matrix

| Area | Initial target |
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

| Framework/pattern | Current status |
| --- | --- |
| Direct C# calls | Current prototype |
| Microsoft.Extensions.DependencyInjection | Current prototype for direct generic registrations, narrow direct-`new` factory registrations, constructor injection, and source interface implementations; broader coverage planned for later alpha work |
| ASP.NET Core MVC | Planned for a later alpha milestone |
| ASP.NET Core Minimal APIs | Planned for a later alpha milestone |
| MediatR | Current preview for source request/notification/handler declarations, `handled_by`, and method-level `Send`/`Publish` call-site flow |
| MCP server | Planned for `0.3.0-alpha.1` |
| EF Core | Planned for `0.4.0-alpha.1` |
| Reflection/assembly scanning | Planned for `0.4.0-alpha.1` |
| Rust/native interop boundaries | Future |

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

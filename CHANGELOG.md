# Changelog

All notable changes to Meridian should be documented in this file.

Meridian follows prerelease SemVer while the project is in alpha.

## Unreleased

### Added

- Initial public documentation plan.
- Architecture direction for a Roslyn-based .NET application-flow graph generator.
- Roadmap using `0.x.0-alpha.N` prerelease versions.
- Planned analyzer scope for ASP.NET Core, expanded dependency injection, MediatR, EF Core, reflection, and Rust/native interop boundaries.
- Analyzer execution pipeline direction for ordered cross-framework facts.
- Planned MCP server preview milestone.
- Golden-file analyzer testing strategy.
- Performance and compatibility documentation.
- Type nodes for source-resolved classes and interfaces.
- `contains` edges from types to ordinary source methods.
- Source interface `implemented_by` edges.
- Constructor `injects` edges for unambiguous source class/interface dependencies.
- Direct generic DI `registered_as` edges for `AddScoped`, `AddSingleton`, and `AddTransient`.
- Generated/bin/obj source filtering by default.
- Dependency-injection sample project and golden-file analyzer test.
- MediatR declaration preview nodes for source-resolved requests, stream requests, notifications, and handlers.
- MediatR `handled_by` edges from request, stream request, and notification types to source handler types, including handled message nodes without analyzable source metadata.
- MediatR `Send`/`Publish` call-site preview for `IMediator`, `ISender`, and `IPublisher`.
- MediatR `sends` and `publishes` edges from enclosing source methods to resolved request or notification types.
- MediatR sample project and golden-file analyzer test coverage for dispatcher call sites.

### Changed

- Split the Roslyn analyzer internals into focused loading, source filtering, graph factory, direct-call, type-declaration, and DI analyzer components.
- Updated the prototype generator/package version to `0.2.0-alpha.2` for the MediatR call-site preview output.

## 0.1.0-alpha.1 — Planned

### Planned

- Core graph model.
- Schema version `0.1`.
- Roslyn solution/project loading.
- Direct method call extraction.
- JSON export.
- Initial `scan`, `explain`, and `path` CLI commands.
- Golden-file analyzer test infrastructure.

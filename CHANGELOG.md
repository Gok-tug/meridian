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
- Ambiguity-aware CLI node resolution for `explain` and `path`, including candidate output for multiple top-scoring matches.
- Narrow DI factory registration support for expression-bodied and safe block-bodied lambdas that directly create source-resolved implementations.
- `Meridian.Mcp` preview server over generated `graph.json` files.
- `meridian mcp --graph <graph.json>` CLI command for local stdio MCP clients.
- MCP graph tools for schema discovery, typed graph search, node lookup, neighbors, shortest paths, path explanations, entrypoint listing, and reverse flow lookup.
- MCP result truncation metadata, stale-graph notes, and explicit endpoint-analyzer limitation responses for agent safety.
- MCP `reload_graph` support so a running MCP server can reread the configured graph file after `meridian scan` regenerates it.
- Agent playbook guidance for MCP freshness, node ID resolution, truncation, and unsupported analyzer limitations.
- MCP tool tests over in-memory fixture graphs.

### Changed

- Split the Roslyn analyzer internals into focused loading, source filtering, graph factory, direct-call, type-declaration, and DI analyzer components.
- Updated the prototype generator/package version to `0.3.0-alpha.2` for MCP freshness and agent-hardening output.
- Revised the roadmap with `0.3.0-alpha.2` as MCP freshness and agent-hardening work before EF Core/reflection analyzer expansion.

## 0.1.0-alpha.1 — Planned

### Planned

- Core graph model.
- Schema version `0.1`.
- Roslyn solution/project loading.
- Direct method call extraction.
- JSON export.
- Initial `scan`, `explain`, and `path` CLI commands.
- Golden-file analyzer test infrastructure.

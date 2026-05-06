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
- MCP result truncation metadata, stale-graph notes, and explicit graph-specific endpoint coverage notes for agent safety.
- MCP `reload_graph` support so a running MCP server can reread the configured graph file after `meridian scan` regenerates it.
- Agent playbook guidance for MCP freshness, node ID resolution, truncation, and unsupported analyzer limitations.
- MCP tool tests over in-memory fixture graphs.
- MCP graph load limits for graph JSON size, node count, edge count, and diagnostic count.
- Process-level CLI tests for help, usage errors, scan smoke output, and scan trust-boundary behavior.
- GitHub Actions CI for restore, build, test, format, vulnerability, and pack checks.
- EF Core preview analyzer for source `DbContext` nodes, `DbSet<TEntity>` containment, DbContext usage, method-level `queries` edges, and direct method-level `writes` edges.
- Static reflection preview analyzer for `typeof(T)` and `Activator.CreateInstance` targets, including diagnostics for runtime-only reflection targets.
- EF Core and dynamic-wiring sample projects with golden-file analyzer coverage.
- MCP schema discovery values for `dbcontext`, `queries`, `writes`, and `reflects`.
- MCP `get_schema` usage hints plus node-kind and relation counts so agents can choose compact queries, evidence opt-in, and relation exclusions more reliably.
- MCP relation exclusion filters for broad graph searches and traversals, including `contains` filtering for noisy neighbor queries.
- Member graph preview nodes for source enums, enum members, properties, and fields.
- Ordinary-method member-reference edges with conservative `reads`, `writes`, and `uses` relations for directly resolved source members, enum references, and `nameof(...)` references.
- `Sample.MemberGraph` with golden-file analyzer coverage for member declarations, member access, enum usage, and feature-planning naming patterns.
- MCP `get_symbol_summary` for compact symbol context with relation counts, contained members, interface/DI links, and follow-up queries.
- MCP `plan_feature` for deterministic graph-guided feature-planning navigation when a requested new concept may be absent from the graph.
- Core graph statistics and agent summary services for deterministic graph metadata, counts, central nodes, extension points, clusters, limitations, and suggested queries.
- MCP `get_graph_statistics` and `get_agent_summary` tools for compact graph orientation before broad traversal or source reading.
- `meridian agent-summary` CLI command with text and JSON output over an existing `graph.json`.
- ASP.NET Core endpoint analyzer preview for MVC route attributes, Minimal API `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch` calls, simple local `MapGroup` prefixes, FastEndpoints `Configure()` verbs, and MinimalApi.Endpoint-style `AddRoute` handlers.
- Synthetic `endpoint` graph nodes with `calls`, `sends`, and `publishes` edges for statically resolved endpoint flow facts.
- `Mediator` namespace support alongside MediatR for source request, command, query, notification, handler, `Send`, and `Publish` patterns.
- Workspace diagnostic severity mapping so non-fatal MSBuild and NuGet warnings stay visible without being surfaced as errors.
- `Sample.AspNetCoreFlow` plus analyzer golden coverage and CLI smoke coverage for endpoint and mediator flow.
- Direct Microsoft DI `GetRequiredService<TImplementation>()` factory alias support for source-resolved registrations.
- CrossMacro dogfood accuracy findings covering graph precision, recall gaps, and agent-summary context size.
- Agent quickstart, portable `AGENTS.md` guidance, and a minimal Claude Code skill preview for MCP-first Meridian workflows.
- `meridian scan --metrics` sidecar output for repeatable scan timings, graph counts, diagnostics, and environment metadata.
- `scripts/dogfood-baseline.ps1` for pinned external repository dogfood baselines with metrics and compact agent summaries.
- PR-safe MCP payload-size guard tests for compact versus evidence-included responses, relation exclusion, truncation notes, and summary caps.
- Graph summary growth sanity tests for compact caps, duplicate structural edge deduplication, and truncation metadata.
- Isolated `tests/Meridian.Benchmarks` BenchmarkDotNet harness for graph summaries and representative MCP payload serialization.
- MCP payload report generation under `artifacts/benchmarks/mcp-payloads.json`.
- Manual/scheduled benchmark workflow with optional dogfood artifact capture outside normal PR CI.
- `Meridian.CI.slnf` solution filter for fast CI/release validation without BenchmarkDotNet.

### Changed

- Split the Roslyn analyzer internals into focused loading, source filtering, graph factory, direct-call, type-declaration, and DI analyzer components.
- Updated the prototype generator/package version to `0.4.0-alpha.1` for EF Core and static dynamic-wiring preview output.
- Revised the roadmap with `0.4.0-alpha.1` as a focused EF Core/static reflection preview before broader assembly scanning and dynamic inference.
- `query_graph` now returns empty edge results when a requested node filter matches no nodes instead of broad relation results.
- MCP required blank inputs now return structured `invalid_input` responses instead of unstructured argument exceptions.
- `meridian scan` now makes the MSBuild project-evaluation trust boundary explicit and supports `--trust-project` to suppress the warning for trusted repositories.
- MCP bulk graph tools now omit edge evidence by default to reduce agent context usage while keeping evidence available through `includeEvidence: true`.
- Updated the prototype generator/package version to `0.4.0-alpha.2` for member graph and feature-planning MCP preview output.
- Updated the prototype generator/package version to `0.4.0-alpha.3` for agent summaries and graph-guided workflow output.
- Updated the prototype generator/package version to `0.4.0-alpha.4` for real ASP.NET flow coverage output.
- Updated the prototype generator/package version to `0.5.0-alpha.1` for dogfood metrics and performance baseline output.
- Updated the prototype generator/package version to `0.5.0-alpha.2` for benchmark, payload measurement, and CI hardening output.
- Updated normal CI and release validation to use `Meridian.CI.slnf`, validate packed CLI artifacts, run local tool smoke tests, and fail on generated non-ignored files.
- Updated MCP and summary endpoint-absence wording to describe stale, old, non-web, or unsupported graphs instead of implying endpoint analyzers are absent.
- Updated agent-summary central-node, extension-point, and cluster ranking to score distinct structural non-containment edges while preserving raw graph evidence edges and statistics.

## 0.1.0-alpha.1 — Planned

### Planned

- Core graph model.
- Schema version `0.1`.
- Roslyn solution/project loading.
- Direct method call extraction.
- JSON export.
- Initial `scan`, `explain`, and `path` CLI commands.
- Golden-file analyzer test infrastructure.

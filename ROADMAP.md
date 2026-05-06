# Meridian Roadmap

Meridian uses prerelease SemVer during early development.

Valid alpha versions:

```text
0.1.0-alpha.1
0.1.0-alpha.2
0.2.0-alpha.1
0.2.0-alpha.2
0.2.0-alpha.3
0.3.0-alpha.1
0.3.0-alpha.2
0.4.0-alpha.1
0.4.0-alpha.2
0.4.0-alpha.3
0.4.0-alpha.4
0.5.0-alpha.1
0.5.0-alpha.2
0.5.0-alpha.3
0.6.0-alpha.1
0.7.0-alpha.1
```

Stable versions such as `0.1.0` should not be published until the package has a tested analyzer pipeline and documented graph contract.

## 0.1.0-alpha.1 — Core graph and Roslyn foundation

Goal: prove that Meridian can load .NET code and emit a deterministic graph with evidence.

Scope:

- Core graph model
- Schema version `0.1`
- Roslyn solution/project loading
- project, document, syntax tree, and symbol indexing
- direct method call edges
- source location mapping
- JSON export
- initial CLI commands:
  - `meridian scan`
  - `meridian explain`
  - `meridian path` over the direct-call graph
- golden-file analyzer test infrastructure
- initial README, architecture, roadmap, and docs

Not in scope:

- type graph enrichment
- dependency injection flow
- full ASP.NET Core flow
- MediatR flow
- EF Core flow
- MCP server
- full reflection resolution

## 0.1.0-alpha.2 — Type graph and DI preview

Goal: make the Roslyn foundation useful for object-oriented and DI-heavy .NET applications before adding larger framework analyzers.

Scope:

- type nodes for source-resolved classes and interfaces
- `contains` edges from types to ordinary source methods
- source interface `implemented_by` edges
- constructor `injects` edges for source class/interface dependencies
- direct generic DI `registered_as` edges for `AddScoped`, `AddSingleton`, and `AddTransient`
- generated/bin/obj source filtering by default
- dependency-injection sample project and golden-file analyzer test
- CLI `path` traversal over emitted type, call, and DI edges

Not in scope:

- non-generic DI registrations
- factory registrations
- keyed services
- Scrutor or assembly scanning
- full runtime DI container behavior

## 0.2.0-alpha.1 — MediatR declaration preview

Goal: add the first framework-aware MediatR graph facts without claiming runtime request dispatch behavior.

Scope:

- MediatR declaration analyzer for source-resolved symbols:
  - `IRequest<TResponse>`
  - `IRequest`
  - `INotification`
  - `IStreamRequest<TResponse>`
  - `IRequestHandler<TRequest, TResponse>`
  - `IRequestHandler<TRequest>`
  - `INotificationHandler<TNotification>`
  - `IStreamRequestHandler<TRequest, TResponse>`
- specialized node kinds:
  - `mediatr_request`
  - `mediatr_notification`
  - `mediatr_handler`
- `handled_by` edges from request, stream request, and notification types to handler types
- MediatR sample project and golden-file analyzer test
- CLI `path` traversal over emitted MediatR declaration edges

Not in scope:

- `IMediator.Send`, `ISender.Send`, or `IPublisher.Publish`
- ASP.NET Core MVC endpoint analyzer
- ASP.NET Core Minimal API analyzer
- expanded Microsoft.Extensions.DependencyInjection registration coverage
- framework-aware path results that stitch endpoint, DI, and MediatR call-site edges

## 0.2.0-alpha.2 — MediatR call-site preview

Goal: connect source methods that dispatch MediatR messages to the request or notification types they send.

Scope:

- `IMediator.Send` and `ISender.Send` call-site detection
- `IMediator.Publish` and `IPublisher.Publish` call-site detection
- `sends` edges from enclosing methods to request types
- `publishes` edges from enclosing methods to notification types
- supported message resolution for inline object creation, in-scope local object creation before dispatch, and concrete parameter static type fallback
- MediatR dispatcher sample coverage and golden-file analyzer test updates
- CLI `path` traversal from method/type callers through `sends` or `publishes` into existing `handled_by` edges

Not in scope:

- ASP.NET Core MVC or Minimal API endpoint bridging
- MediatR `CreateStream`
- interprocedural request tracking
- runtime-created request or notification objects
- direct method-to-handler shortcut edges
- expanded Microsoft.Extensions.DependencyInjection registration coverage

## 0.2.0-alpha.3 — .NET flow hardening

Goal: harden existing .NET graph quality from real-project validation before exposing graph queries through MCP.

Scope:

- ambiguity-aware node resolution for `meridian explain` and `meridian path`
- candidate output when short labels or symbols match multiple top-scoring nodes
- exact node ID matching remains an unambiguous resolution path
- narrow DI factory `registered_as` edges when a generic factory lambda directly returns `new Implementation(...)`
- expression-bodied factory lambdas and block-bodied lambdas that end with one top-level direct `return new Implementation(...);`
- dependency-injection sample and golden-file updates for factory registrations
- validation notes from real-project scans without hard-coding project-specific behavior

Not in scope:

- ASP.NET Core MVC or Minimal API endpoint analyzers
- endpoint-to-MediatR bridging
- MediatR `CreateStream`
- interprocedural request tracking
- non-generic DI registrations
- keyed services, Scrutor scanning, or runtime DI container execution
- EF Core, reflection, assembly scanning, MCP server, or Rust/native interop implementation

Later alpha analyzer work should add:

- source-resolved non-generic DI registrations and diagnostics
- framework-aware path ranking across endpoint, DI, mediator, and direct-call edges

## 0.3.0-alpha.1 — MCP server preview

Goal: make Meridian usable by AI agents through a compact graph-query interface.

Scope:

- `Meridian.Mcp` package/project
- local MCP server over generated graph files
- initial MCP tools:
  - `get_schema`
  - `query_graph`
  - `get_node`
  - `get_neighbors`
  - `shortest_path`
  - `explain_path`
  - `list_entrypoints`
  - `find_flows_to_symbol`
- CLI/MCP documentation
- agent usage examples
- MCP tests over fixture graph JSON files

## 0.3.0-alpha.2 — MCP freshness and agent hardening

Goal: make the MCP preview trustworthy for iterative agent workflows without expanding analyzer scope.

Scope:

- `reload_graph` MCP tool that rereads the configured `--graph` file into the running MCP server
- reloadable MCP graph state that atomically swaps complete graph snapshots
- reload failure behavior that preserves the previous active graph
- reload response metadata with previous/new graph counts, generator version, graph path, load timestamp, file timestamp, and failure messages
- agent playbook for graph-guided CLI/MCP usage
- explicit node ID strategy for agents:
  - never invent node IDs
  - use `get_schema` for available kinds and relations
  - use `query_graph` or returned candidates to discover exact IDs before path or neighbor tools
- freshness protocol:
  - after source edits, run `meridian scan`
  - then call `reload_graph` or restart the MCP server before trusting changed-code graph results
- CLI smoke-test requirements that validate generated graph contents, not only exit codes
- preview contract for planned human-readable `summary`, `tree`, and `report` outputs derived from `graph.json`
- honest hook/watch limitation wording: automatic watch is not implemented; future automation must trigger both scan and MCP reload

Not in scope:

- ASP.NET Core MVC or Minimal API endpoint analyzers
- EF Core analyzer
- reflection or assembly scanning implementation
- live Roslyn analysis inside MCP tools
- automatic FileSystemWatcher hot-reload as the default freshness mechanism
- broad multi-language analysis
- Rust/native interop implementation

## 0.4.0-alpha.1 — EF Core and dynamic wiring

Goal: expand beyond request/handler flow into persistence and dynamic registration patterns.

Scope:

- EF Core analyzer preview:
  - source `DbContext` nodes
  - `DbSet<TEntity>` containment edges
  - `_context.Entities` access
  - `_context.Set<TEntity>()` access
  - method-level `queries` edges to entity types
  - direct method-level `writes` edges to statically resolved entity types
- static reflection analyzer preview:
  - `typeof(T)`
  - `Activator.CreateInstance<T>()`
  - `Activator.CreateInstance(typeof(T))`
  - diagnostics for runtime-only reflection targets instead of guessed edges
- MCP schema discovery for EF Core and reflection graph relations
- MCP agent-efficiency hardening from real-project graph audits:
  - bulk edge evidence omitted by default for `query_graph`, `get_neighbors`, and `find_flows_to_symbol`
  - `includeEvidence` opt-in for detailed file, line, symbol, and reason evidence
  - `excludeRelations` filters applied before traversal and capping so noisy structural edges such as `contains` can be removed
  - `get_schema` usage hints, node-kind counts, and relation counts for more reliable agent query planning
  - safer no-result and no-path wording that treats graph absence as absence from the loaded precomputed graph, not proof of absence in source
- golden-file tests and sample projects for EF Core, static dynamic-wiring, and MCP efficiency patterns

Deferred to later milestones:

- `nameof` reflection heuristics
- `Assembly.Load`, `GetTypes`, and `IsAssignableFrom` inference
- Scrutor-style scanning and MediatR assembly registration scanning
- SQL reconstruction, provider behavior, migrations, and full LINQ expression semantics
- stronger ambiguous edge reporting beyond the current candidate output

## 0.4.0-alpha.2 — Member graph and feature-planning MCP

Goal: make Meridian useful for feature-planning questions where the new concept does not exist yet, such as "where should I add a new execution mode?"

Scope:

- member-level source graph preview:
  - `enum` nodes for source-resolved enum declarations
  - `enum_member` nodes for enum values
  - `property` nodes for source properties, especially persisted/domain and ViewModel state
  - `field` nodes for source fields that participate in routing or dependency state
- member containment and reference edges:
  - type-to-member declaration edges
  - ordinary-method static references to source-resolved properties, fields, enum types, and enum members
  - conservative `reads` and `writes` edges for directly resolved member access
- compact symbol summaries for MCP:
  - source file and location
  - implemented interfaces and implementations
  - DI registrations and injection sites
  - important contained methods and members
  - relation counts without full edge payloads
- feature-planning MCP preview tool that returns ranked edit points instead of raw graph arrays:
  - accepts a goal, seed symbols, and optional terms
  - returns likely files/symbols to inspect, why each matters, and follow-up graph queries
  - explicitly handles absent new concepts by looking for existing abstractions, strategies, modes, factories, registries, orchestrators, and executors
- agent playbook guidance for feature planning:
  - do not search only for the new term when the feature is not implemented yet
  - start from existing abstractions and extension points
  - use grep only for missing domain vocabulary or constants not yet represented in the graph
- golden-file coverage over member nodes and feature-planning fixture graphs

Not in scope:

- full interprocedural dataflow
- runtime reflection, dynamic dispatch, or container execution
- XAML/View-ViewModel binding analysis
- full conditional/control-flow semantics
- LLM-generated plans inside Meridian

## 0.4.0-alpha.3 — Agent summaries and graph-guided workflow

Goal: reduce agent context use by producing deterministic summaries and ranked navigation surfaces over `graph.json` without giving up Roslyn semantic precision.

Scope:

- deterministic `agent-summary` output derived from `graph.json`:
  - central abstractions and highly connected services
  - subsystem/community-style clusters where graph structure is strong enough
  - likely extension points based on naming and graph position
  - graph limitations and analyzer blind spots present in the current graph
  - suggested follow-up MCP queries
- MCP summary/statistics tools:
  - compact graph stats with node-kind, relation, and confidence breakdowns
  - symbol summary retrieval without broad neighbor traversal
  - optional token-budgeted response mode for broad exploratory queries
- assistant workflow integration docs:
  - how agents should use Meridian before grep/read
  - how to combine Meridian with targeted grep when a concept is not yet in source
  - how to avoid overclaiming from graph absence
  - feature-planning ranking guardrails: keep rankings conservative, expose reasons and limitations, and avoid hard-coded domain-specific edit orders
- Claude Code skill preview and portable agent guidance for Meridian workflows:
  - package a minimal graph-guided exploration and feature-planning workflow for Claude Code agents
  - document first-run MCP setup, graph verification with `agent-summary`, and OpenCode-like client guidance through the standard stdio MCP command
  - guide agents to start with `get_schema`, use `plan_feature` for absent concepts, and follow with `get_symbol_summary` before reading source
  - reinforce MCP guardrails: graph absence is not source absence, broad traversals should stay capped, evidence should be requested only when needed, and rescans should be user-initiated
  - keep MCP as the capability layer and the skill as workflow guidance, not a replacement for MCP tools
  - defer plugin-style distribution until Meridian has multiple skills, reusable settings, or install targets that justify a packaged plugin
- real-project benchmark report comparing:
  - broad grep/read
  - broad MCP text search
  - targeted MCP graph navigation
  - hybrid Meridian + grep workflow

Not in scope:

- multi-language tree-sitter analysis
- document, PDF, image, video, or URL ingestion
- replacing Roslyn semantic analysis with heuristic extraction
- automatic assistant hook installation across third-party tools

## 0.4.0-alpha.4 — Real ASP.NET flow coverage

Goal: make the graph useful on real ASP.NET Core repositories before performance hardening by adding endpoint entrypoints and `Mediator.SourceGenerator`-style mediator flows surfaced by dogfood scans.

Scope:

- ASP.NET Core endpoint analyzer preview:
  - MVC/controller route attributes including common `[controller]` and `[action]` tokens
  - Minimal API `MapGet`, `MapPost`, `MapPut`, `MapDelete`, and `MapPatch`
  - simple local `MapGroup` prefixes in the same block before route mapping calls
  - FastEndpoints `Configure()` verb calls linked to same-type `ExecuteAsync` or `HandleAsync`
  - MinimalApi.Endpoint-style `AddRoute` methods whose supported `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch` lambda delegates to source `HandleAsync`
- endpoint nodes reuse existing `endpoint` graph kind and existing `calls`, `sends`, `publishes`, and `handled_by` relations
- `Mediator` namespace support alongside MediatR for request, command, query, notification, handler, `Send`, and `Publish` patterns
- workspace diagnostic severity mapping that keeps non-fatal MSBuild/NuGet warnings visible without surfacing them as failures
- ASP.NET flow sample project, analyzer golden coverage, and CLI smoke coverage
- documentation updates from dogfood findings
- alpha4 hardening after CrossMacro dogfood:
  - summary ranking scores distinct structural non-containment edges while raw graph evidence remains intact
  - narrow DI `GetRequiredService<TImplementation>()` factory alias support for source-resolved implementations
  - CrossMacro accuracy audit documentation for desktop/UI/MVVM/native-boundary recall gaps

Not in scope:

- route precedence, authorization/filter/middleware graphing, model binding, runtime route discovery, generated-code execution, arbitrary delegate dataflow, stream mediator dispatch, broad DI factory/dataflow analysis, XAML binding analysis, CommunityToolkit.Mvvm generated member analysis, CLI runtime routing, native boundary analysis, cache, or incremental analysis

## 0.5.0-alpha.1 — Metrics baseline and dogfood repeatability

Goal: make real-repository validation repeatable and measurable before deeper performance, cache, or incremental-analysis work.

Scope:

- CLI `scan --metrics` sidecar output for repeatable baseline timings, graph counts, diagnostics, memory, and environment metadata
- process-level CLI coverage for metrics help text, parseability, non-negative timings, metadata, and graph count consistency
- repeatable dogfood baseline script for pinned CleanArchitecture, eShopOnWeb, and CrossMacro scans
- compact `agent-summary` capture alongside dogfood graph and metrics artifacts
- first `0.5.0-alpha.1` dogfood metrics baseline documented in dogfood and performance docs
- initial memory usage tracking through `peak_working_set_mb`

Not in scope:

- BenchmarkDotNet suite, scheduled benchmark workflow, cache implementation, incremental analysis implementation, or graph schema changes

## 0.5.0-alpha.2 — Benchmarks, payload measurements, and CI hardening

Goal: turn the dogfood baseline into repeatable engineering signals without making normal PR CI slow or network-dependent.

Scope:

- benchmark suite for representative small and medium local fixtures
- large-solution benchmark report using dogfood or explicitly documented external targets
- MCP payload-size benchmarks for realistic agent workflows such as `get_agent_summary`, `get_symbol_summary`, bounded `query_graph`, and path tools
- manual or scheduled dogfood/benchmark workflow that stores artifacts outside normal PR CI
- CI/release hardening for CLI smoke coverage, package validation, vulnerable package checks, and generated artifact hygiene
- large-graph summary sanity checks for duplicate evidence edges, fixture/test skew, and bounded output behavior
- performance documentation updates with benchmark methodology and trend guidance

Benchmark targets:

- small solution: under 30 seconds
- medium solution, 10-25 projects: under 2 minutes
- large solution, 50+ projects / 500k+ LOC: measured and published before stable release

## 0.5.0-alpha.3 — Cache and graph stability design

Goal: de-risk future incremental analysis before implementing caching by documenting stable inputs, invalidation rules, and graph identity guarantees.

Scope:

- cache design with stable file fingerprints, analyzer version inputs, graph schema inputs, package/project inputs, and conservative invalidation
- incremental analysis design for changed projects/files and affected analyzer passes
- graph diff stability review across repeated scans of the same inputs
- stable node ID review for source symbols, endpoint nodes, synthetic nodes, and generated/filtered-source boundaries
- cache-friendly analyzer boundary review so later caching does not require graph contract churn

Not in scope:

- turning cache or incremental analysis on by default before correctness and stale-graph behavior are proven

## 0.6.0-alpha.1 — UI bindings, source-generator graph preview, and conditional flow research

Goal: close major blind spots that matter for desktop/UI-heavy .NET applications, generated MVVM members, and static conditions that select a route.

Scope:

- XAML/View-ViewModel binding analyzer research:
  - Avalonia and WPF view-to-ViewModel association patterns
  - bindings from XAML properties to ViewModel properties
  - command bindings to ViewModel methods or command properties
  - command bindings to CommunityToolkit.Mvvm generated command properties
  - diagnostics when bindings cannot be resolved statically
- CommunityToolkit.Mvvm source-generator-aware graph preview:
  - `[RelayCommand]` generated command property awareness
  - `[ObservableProperty]` generated public property awareness
  - links from generated command properties back to source methods when statically safe
  - diagnostics when generated member targets cannot be resolved
- method-body conditional analysis preview:
  - `if`/`switch` branches over source-resolved enum values and simple constants
  - method-level `switches_on` or `branches_on` edges to properties, fields, enum types, and enum members
  - conservative branch metadata that records the condition text and source location
  - no inferred runtime path when a condition cannot be statically tied to known symbols
- golden-file samples for UI binding and conditional routing patterns

Not in scope:

- complete XAML runtime behavior
- arbitrary binding converters, reflection-heavy bindings, or dynamic DataContext inference
- full source-generator execution or generated-code inclusion by default
- full symbolic execution or path-sensitive dataflow
- proving that a branch is reachable at runtime

## 0.7.0-alpha.1 — Runtime wiring and native boundary preview

Goal: capture common source-visible runtime wiring patterns without executing the app, and expose .NET-side native interop boundaries without claiming native implementation analysis.

Scope:

- CLI command/router/handler runtime wiring patterns where registrations are source-visible and deterministic
- delegate factory resolution for common DI and routing shapes
- `Func<T>` and `Func<TArg,TResult>` factory patterns when the target is source-resolved without executing a container
- .NET-side `DllImport` and `LibraryImport` boundary nodes/edges
- `NativeLibrary.Load` / `NativeLibrary.TryLoad` constant-library hints
- diagnostics for runtime-only library names, resolver dictionaries, or delegate factories that cannot be resolved statically

Not in scope:

- runtime DI container execution
- full runtime command dispatch execution
- full Rust/C/C++ static analysis
- Cargo workspace graphing in this .NET analyzer pass
- native implementation call graphs
- branch-dependent or environment-dependent runtime factory selection

## Future

Potential future analyzer packs and integrations:

- deeper native interop analyzer packs beyond .NET-side boundary detection
- messaging analyzers:
  - MassTransit
  - NServiceBus
  - Azure Service Bus
  - RabbitMQ
- background job analyzers:
  - Hangfire
  - Quartz.NET
- HTTP client analyzers:
  - HttpClientFactory
  - Refit
  - gRPC clients
- validation and mapping analyzers:
  - FluentValidation
  - AutoMapper
- configuration analyzers:
  - Options pattern
  - configuration binding
- OpenTelemetry/export integrations
- richer visual graph explorer
- graph diffing between commits or releases
- broader source-generator-aware analysis beyond CommunityToolkit.Mvvm preview

## Release readiness checklist

Before a public stable release:

- README accurately reflects implemented behavior
- CLI docs match actual command output
- graph schema is versioned
- limitations are explicit
- compatibility matrix is current
- all analyzers have golden-file tests
- CLI smoke tests validate exit codes and graph contents
- MCP freshness workflow and agent playbook are current
- large solution benchmark has been published
- NuGet package metadata is complete
- changelog is updated
- security policy exists

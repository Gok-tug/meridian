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

- ASP.NET Core MVC and Minimal API endpoint analyzers
- endpoint-to-MediatR flow linking
- source-resolved non-generic DI registrations and diagnostics
- framework-aware path ranking across endpoint, DI, MediatR, and direct-call edges

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
- CLI smoke-test coverage that validates generated graph contents, not only exit codes
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

- EF Core analyzer:
  - `DbContext`
  - `DbSet<TEntity>`
  - `_context.Entities`
  - `_context.Set<TEntity>()`
  - query/entity access edges
- reflection analyzer:
  - `typeof`
  - `nameof`
  - `Activator.CreateInstance`
  - `Assembly.Load`
  - `GetTypes`
  - `IsAssignableFrom`
- assembly scanning detection:
  - Scrutor-style scanning
  - MediatR assembly registration
- stronger evidence and confidence reporting
- ambiguous edge reporting

## 0.5.0-alpha.1 — Performance and hardening

Goal: prepare Meridian for large real-world solutions.

Scope:

- benchmark suite
- large solution benchmark report
- cache design
- incremental analysis design
- CI hardening
- graph diff stability
- memory usage tracking
- performance documentation updates

Benchmark targets:

- small solution: under 30 seconds
- medium solution, 10-25 projects: under 2 minutes
- large solution, 50+ projects / 500k+ LOC: measured and published before stable release

## Future

Potential future analyzer packs and integrations:

- Rust/native interop boundary detection
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
- source-generator-aware analysis

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

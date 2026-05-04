# Meridian Roadmap

Meridian uses prerelease SemVer during early development.

Valid alpha versions:

```text
0.1.0-alpha.1
0.1.0-alpha.2
0.2.0-alpha.1
0.3.0-alpha.1
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

## 0.2.0-alpha.1 — ASP.NET Core, expanded DI, and MediatR flow

Goal: make Meridian useful for common real-world .NET application flow.

Scope:

- deterministic analyzer execution pipeline for cross-framework facts
- ASP.NET Core MVC endpoint analyzer
- ASP.NET Core Minimal API analyzer
- expanded Microsoft.Extensions.DependencyInjection analyzer coverage:
  - non-generic registration overloads where symbols can be resolved
  - duplicate registrations and diagnostics
  - service resolution facts reusable by other analyzers
- MediatR analyzer:
  - `IRequest<TResponse>`
  - `IRequest`
  - `INotification`
  - `IRequestHandler<TRequest, TResponse>`
  - `IRequestHandler<TRequest>`
  - `INotificationHandler<TNotification>`
  - `IMediator.Send`
  - `ISender.Send`
  - `IPublisher.Publish`
- framework-aware path results that include endpoint, DI, and MediatR edges
- sample applications:
  - `SampleApi.MediatR`
  - `SampleApi.MinimalApi`
- golden-file tests for ASP.NET Core, expanded DI, and MediatR analyzers

## 0.3.0-alpha.1 — MCP server preview

Goal: make Meridian usable by AI agents through a compact graph-query interface.

Scope:

- `Meridian.Mcp` package/project
- local MCP server over generated graph files
- initial MCP tools:
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
- large solution benchmark has been published
- NuGet package metadata is complete
- changelog is updated
- security policy exists

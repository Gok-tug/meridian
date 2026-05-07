# Meridian

Meridian is a semantic flow graph generator for .NET applications.

It maps endpoints, handlers, dependency injection registrations, service calls, database access, messaging-style flows, reflection, and dynamic wiring into a queryable graph for humans and AI agents.

Meridian is not intended to be a generic C# call graph visualizer. The goal is to explain real application flow in modern .NET systems where important behavior is often expressed through frameworks such as ASP.NET Core, Microsoft.Extensions.DependencyInjection, MediatR, Entity Framework Core, source generators, and reflection-based registration patterns.

## Status

Meridian is in early alpha design/prototype stage.

The NuGet package is published as prerelease alpha builds, but the analyzer implementation is not stable yet. The current package version is set in `src/Meridian.Cli/Meridian.Cli.csproj`; completed release history is tracked in [CHANGELOG.md](CHANGELOG.md), and upcoming milestones are tracked in [ROADMAP.md](ROADMAP.md).

Do not treat `0.x` alpha releases as production-ready or schema-stable.

## Why Meridian exists

Large .NET applications are difficult to understand from direct method calls alone.

A request can enter through an ASP.NET Core endpoint, create a MediatR request, resolve a handler through dependency injection, call an interface, use an EF Core DbContext, publish notifications, and rely on assembly scanning or reflection. Traditional call graph tools usually miss these framework-level links.

Meridian aims to make those links explicit:

```text
GET /orders/{id}
  --sends--> GetOrderQuery
  --handled_by--> GetOrderQueryHandler
  --injects--> IOrderRepository
  --implemented_by--> EfOrderRepository
  --uses--> OrderDbContext
  --queries--> Order
  --writes--> Order
```

## Design principles

- Semantic first: use Roslyn symbols and MSBuild project context instead of text search.
- Framework aware: model ASP.NET Core, DI, MediatR, EF Core, reflection, and assembly scanning as first-class flow concepts.
- Evidence based: every important node and edge should explain where it came from.
- Confidence aware: distinguish direct symbol facts from inferred or ambiguous runtime behavior.
- Agent ready: produce graph data that humans, CLIs, and MCP-enabled AI agents can query.
- Extensible: analyzer packs should plug into a shared graph model rather than being hard-coded into one monolith.

## CLI

```bash
meridian scan MyApp.sln
meridian explain "GetOrderQuery"
meridian path "GET /orders/{id}" "OrderDbContext"
meridian agent-summary --graph meridian-out/graph.json
meridian mcp --graph meridian-out/graph.json
```

The `path` command is a core use case. It finds and explains an application-flow route between two symbols, routes, files, or graph nodes.

Example:

```bash
meridian path "GET /orders/{id}" "OrderDbContext"
```

Expected output:

```text
GET /orders/{id}
  --sends--> GetOrderQuery
  --handled_by--> GetOrderQueryHandler
  --injects--> IOrderRepository
  --implemented_by--> EfOrderRepository
  --uses--> OrderDbContext
```

See [docs/cli.md](docs/cli.md) for the command contract. See [docs/agent-quickstart.md](docs/agent-quickstart.md) for connecting Meridian to MCP-enabled coding agents.

## Analyzer scope

The first useful version should focus on application flow, not every possible C# construct.

Implemented preview support:

- Roslyn solution/project loading
- Type, method, enum, enum member, property, and field nodes with `contains` edges
- Direct method `calls` edges
- Ordinary-method `reads`, `writes`, and `uses` edges for directly resolved source members and enum references
- Method-level conditional-flow preview with `branches_on` and `switches_on` edges for directly resolved symbols in simple `if` and `switch` conditions
- CommunityToolkit.Mvvm source-generator preview for `[ObservableProperty]` generated properties, `[RelayCommand]` generated `mvvm_command` nodes, and `generated_from` edges
- Avalonia AXAML static binding preview for typed scopes, simple `{Binding ...}` / `{CompiledBinding ...}` member paths, generated Toolkit properties and commands, and `binds_to` edges
- DI `injects`, `implemented_by`, direct generic `registered_as`, narrow direct-`new` factory `registered_as`, and direct `GetRequiredService<TImplementation>()` factory alias edges for source-resolved symbols
- ASP.NET Core endpoint preview for MVC route attributes, Minimal API `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch`, simple `MapGroup` prefixes, FastEndpoints `Configure()` verbs, and MinimalApi.Endpoint-style `AddRoute` methods
- MediatR and `Mediator.SourceGenerator`-style declaration and method-level call-site preview with `mediatr_request`, `mediatr_notification`, `mediatr_handler`, `handled_by`, `sends`, and `publishes`
- EF Core preview for source `DbContext`, `DbSet<TEntity>` containment, `_context.Entities`, `_context.Set<TEntity>()`, method-level `queries` edges, and direct method-level `writes` edges
- Static reflection preview for `typeof(T)` and `Activator.CreateInstance` targets, with diagnostics for runtime-only targets
- JSON graph export
- `scan`, `explain`, `path`, `agent-summary`, and `mcp` CLI commands, including ambiguity reporting when a short query matches multiple top-scoring nodes
- MCP server preview over generated `graph.json` files with schema discovery, typed graph queries, bounded results, compact graph statistics, agent summaries, compact symbol summaries, deterministic feature-planning navigation, `reload_graph` refresh support for running MCP servers, stale-graph notes, and graph-specific endpoint coverage notes
- Golden-file analyzer tests and MCP tool tests

Planned follow-up work:

- Broader ASP.NET Core routing semantics such as routing precedence, filters, middleware, authorization, model binding, runtime route discovery, and arbitrary delegate dataflow
- Additional mediator dispatch patterns such as `CreateStream`, interprocedural request tracking, and runtime object construction diagnostics
- Broader EF Core query semantics beyond static DbContext/DbSet entity access
- Reflection assembly scanning patterns such as Scrutor, `Assembly.Load`, `GetTypes`, and `IsAssignableFrom`
- Additional human-readable graph views such as `tree` and `report`
- Incremental analysis and caching
- UI binding analysis beyond conservative typed Avalonia AXAML scopes
- CLI/runtime command routing and delegate factory patterns
- Rust/native interop boundary detection

Known limitations: full ASP.NET Core routing precedence, authorization/filter/middleware graphing, model binding, runtime route discovery, arbitrary endpoint delegate dataflow, mediator `CreateStream`, interprocedural/runtime mediator dispatch tracking, full EF Core query semantics, full reflection resolution, assembly scanning, broad DI factory/dataflow analysis, XAML runtime binding behavior beyond typed Avalonia AXAML preview scopes, full CommunityToolkit.Mvvm source-generator behavior beyond `[ObservableProperty]`/`[RelayCommand]` hints, path-sensitive conditional analysis, CLI runtime/delegate routing, automatic watch/hot-reload, planned `tree`/`report` views, and incremental/cached analysis.

Rust support is not part of the .NET MVP as a full Rust static analyzer. It is planned first as .NET-to-native/Rust interop detection for applications that cross FFI boundaries through `DllImport`, `LibraryImport`, native DLLs, or generated bindings.

## Output model

Meridian produces a versioned graph document:

```json
{
  "schema_version": "0.1",
  "generator": "Meridian",
  "generator_version": "<meridian-version>",
  "nodes": [],
  "edges": []
}
```

Edges carry relation and confidence metadata:

```json
{
  "source": "method:myapp.orders.orderscontroller.getbyid",
  "target": "type:myapp.orders.getorderquery",
  "relation": "sends",
  "confidence": "EXTRACTED",
  "confidence_score": 1.0,
  "evidence": {
    "file": "OrdersController.cs",
    "line": 42,
    "reason": "Roslyn resolved mediator Send call to 'MyApp.Orders.GetOrderQuery' from inline object creation."
  }
}
```

See [docs/graph-model.md](docs/graph-model.md) and [docs/confidence-model.md](docs/confidence-model.md).

## Performance expectations

Meridian is intended for real-world .NET solutions, but early alpha versions should be honest about Roslyn workspace cost.

Initial benchmark targets:

- Small solution: under 30 seconds
- Medium solution, 10-25 projects: under 2 minutes
- Large solution, 50+ projects / 500k+ LOC: measured and published before stable release

Meridian should track workspace load time, compilation time, analyzer time, graph construction time, export time, cache hit rate, and peak memory usage separately.

See [docs/performance.md](docs/performance.md).

## Documentation

Start here:

- [Architecture](ARCHITECTURE.md)
- [Vision](docs/vision.md)
- [Roadmap](ROADMAP.md)
- [Changelog](CHANGELOG.md)

Using Meridian:

- [CLI](docs/cli.md)
- [MCP server](docs/mcp.md)
- [Agent quickstart](docs/agent-quickstart.md)
- [Agent playbook](docs/agent-playbook.md)

Reference:

- [Graph model](docs/graph-model.md)
- [Analyzer coverage](docs/analyzers.md)
- [Confidence model](docs/confidence-model.md)
- [Compatibility](docs/compatibility.md)
- [Known limitations](docs/limitations.md)

Project process:

- [Testing](docs/testing.md)
- [Performance](docs/performance.md)
- [Releasing](docs/releasing.md)
- [Rust/native interop scope](docs/rust-interop.md)
- [Dogfood audit](docs/dogfood-audit.md)

## License

Meridian is planned as an MIT-licensed project. See [LICENSE](LICENSE).

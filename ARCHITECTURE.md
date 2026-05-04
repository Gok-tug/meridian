# Meridian Architecture

Meridian is designed as an extensible .NET application-flow graph platform.

Roslyn provides the semantic foundation. Analyzer packs understand framework conventions and add higher-level application-flow edges on top of the Roslyn symbol graph.

## Goals

- Load real .NET solutions and projects through MSBuild/Roslyn.
- Build a deterministic, versioned graph of application flow.
- Model framework-mediated behavior that plain call graphs miss.
- Preserve source evidence for every important graph fact.
- Distinguish extracted, inferred, and ambiguous relationships.
- Support CLI, JSON export, future visualizations, and MCP-based agent queries.
- Keep analyzers modular so MediatR, EF Core, ASP.NET Core, and other frameworks are not hard-coded into the core.

## Non-goals

- Meridian is not a full compiler replacement.
- Meridian is not a runtime profiler.
- Meridian is not initially a full multi-language static analyzer.
- Meridian is not initially a full Rust call graph tool.
- Meridian should not claim complete accuracy for reflection, source generators, dynamic dispatch, or runtime-only registrations.

## High-level pipeline

```text
load solution
  -> create Roslyn workspace context
  -> index projects, compilations, symbols, syntax trees
  -> run analyzer packs
  -> build graph
  -> normalize and validate graph
  -> export/query/report
```

## Proposed project layout

```text
Meridian.sln
src/
  Meridian.Cli/
  Meridian.Abstractions/
  Meridian.Core/
  Meridian.Roslyn/
  Meridian.Analyzers.AspNetCore/
  Meridian.Analyzers.DependencyInjection/
  Meridian.Analyzers.MediatR/
  Meridian.Analyzers.EntityFrameworkCore/
  Meridian.Analyzers.Reflection/
  Meridian.Exporters.Json/
  Meridian.Exporters.Mermaid/
  Meridian.Exporters.Dgml/
  Meridian.Mcp/

tests/
  Meridian.Core.Tests/
  Meridian.AnalyzerTests/
  Meridian.Cli.Tests/
  Meridian.Mcp.Tests/
  Meridian.Benchmarks/

samples/
  SampleApi.MediatR/
  SampleApi.MinimalApi/
  SampleApi.EfCore/
  SampleApi.Reflection/
  SampleApi.NativeInterop/
```

The current repository may start smaller, but this is the public architecture direction.

## Core packages

### Meridian.Abstractions

Contains public contracts shared by analyzers and exporters:

- graph node model
- graph edge model
- confidence model
- evidence model
- analyzer interface
- exporter interface
- diagnostic model

### Meridian.Core

Owns graph construction and validation:

- node identity rules
- edge normalization
- duplicate merging
- graph validation
- deterministic ordering
- schema versioning
- query primitives such as shortest path and neighbor lookup

### Meridian.Roslyn

Owns Roslyn integration:

- `MSBuildWorkspace` loading
- project and document indexing
- compilation access
- semantic model access
- symbol lookup
- operation walking
- source location mapping
- direct call extraction

### Meridian.Analyzers.*

Analyzer packs add framework-specific knowledge:

- ASP.NET Core endpoints
- dependency injection registrations
- constructor injection
- MediatR request/handler/message flows
- EF Core DbContext and entity usage
- reflection and assembly scanning

### Meridian.Exporters.*

Exporters convert the graph into external formats:

- JSON
- Mermaid
- DGML
- future HTML or graph explorer output

### Meridian.Mcp

Provides MCP tools over a generated Meridian graph. This should be introduced early as `0.3.0-alpha.1` because AI-agent integration is a primary use case.

## Analyzer contract

A future analyzer API should look conceptually like this:

```csharp
public interface IMeridianAnalyzer
{
    string Name { get; }
    Task AnalyzeAsync(
        AnalysisContext context,
        GraphBuilder graph,
        CancellationToken cancellationToken);
}
```

Analyzers should not write files directly. They should add graph facts with evidence and diagnostics.

## Graph identity

Nodes need stable IDs so golden tests, diffs, and MCP queries remain reliable.

Recommended identity sources:

- symbol display string for C# symbols
- route template for endpoints
- fully qualified type name for framework concepts
- stable generated ID for non-symbol concepts

Example:

```text
endpoint:GET:/orders/{id}
type:MyApp.Features.Orders.GetOrderQuery
method:MyApp.Features.Orders.GetOrderQueryHandler.Handle
service:MyApp.Data.OrderDbContext
```

## Confidence model

Each edge must declare how certain Meridian is:

```text
EXTRACTED   direct semantic evidence from Roslyn or framework metadata
INFERRED    pattern-based evidence with strong but indirect support
AMBIGUOUS   multiple possible targets or runtime behavior required
```

Confidence is part of the graph contract, not a UI decoration.

## Evidence model

Important nodes and edges should carry evidence:

```json
{
  "file": "Features/Orders/GetOrderQuery.cs",
  "line": 42,
  "symbol": "MyApp.Features.Orders.GetOrderQueryHandler.Handle",
  "reason": "IRequestHandler<GetOrderQuery, OrderDto>"
}
```

Evidence lets users audit the graph and lets AI agents cite why a flow exists.

## CLI and query layer

The CLI should operate over the same graph model as MCP:

```text
scan      build graph
explain   explain one node or symbol
path      explain a path between two nodes or symbols
query     natural-language or structured graph query, later backed by MCP/agent integration
```

## MCP layer

Initial MCP tools:

```text
query_graph
get_node
get_neighbors
shortest_path
explain_path
list_entrypoints
find_flows_to_symbol
```

MCP should read a generated graph and return compact, evidence-bearing answers.

## Testing architecture

Analyzer correctness should be proven with golden-file tests:

```text
tests/
  Meridian.AnalyzerTests/
    MediatR/
      Fixtures/
      Expected/
```

Each analyzer test loads a fixture project, runs the analyzer set, normalizes the graph, and compares it against an expected `.graph.json` file.

## Performance architecture

Large-solution performance depends heavily on Roslyn workspace loading. Meridian should measure phases separately:

```text
workspace_load_ms
compilation_ms
symbol_index_ms
analyzer_ms
graph_build_ms
export_ms
peak_memory_mb
cache_hit_rate
```

Caching and incremental analysis should be planned before stable release, not treated as a late afterthought.

# Testing Strategy

Meridian's credibility depends on analyzer correctness.

Every analyzer should have golden-file tests that compare generated graph output against expected graph JSON.

## Test layout

```text
tests/
  Meridian.Core.Tests/
  Meridian.AnalyzerTests/
    AspNetCore/
      Fixtures/
      Expected/
    DependencyInjection/
      Fixtures/
      Expected/
    MediatR/
      Fixtures/
      Expected/
    EntityFrameworkCore/
      Fixtures/
      Expected/
    Reflection/
      Fixtures/
      Expected/
  Meridian.Cli.Tests/
  Meridian.Mcp.Tests/
  Meridian.Benchmarks/
```

Example:

```text
tests/
  Meridian.AnalyzerTests/
    MediatR/
      Fixtures/
        GetOrderQuery/
          GetOrderQuery.csproj
          Program.cs
          GetOrderQuery.cs
          GetOrderQueryHandler.cs
      Expected/
        GetOrderQuery.graph.json
      GetOrderQueryTests.cs
```

## Golden-file test flow

Each analyzer test should:

1. load a fixture solution or project,
2. create the Roslyn analysis context,
3. run one analyzer or a known analyzer set,
4. build the graph,
5. normalize deterministic fields,
6. compare to expected `.graph.json`,
7. assert diagnostics when relevant.

## Normalization

Golden output must be stable across machines.

Normalize:

- path separators,
- absolute paths to fixture-relative paths,
- node ordering,
- edge ordering,
- generated timestamps,
- environment-specific SDK paths.

Do not remove meaningful evidence just to make tests easier.

## What golden files should assert

A useful golden file should include:

- node IDs,
- node kinds,
- labels,
- edge relations,
- confidence categories,
- evidence reason,
- diagnostics for ambiguity.

Example expected edge:

```json
{
  "source": "type:MyApp.GetOrderQuery",
  "target": "type:MyApp.GetOrderQueryHandler",
  "relation": "handled_by",
  "confidence": "EXTRACTED",
  "confidence_score": 1.0,
  "evidence": {
    "file": "GetOrderQueryHandler.cs",
    "line": 7,
    "reason": "IRequestHandler<GetOrderQuery, OrderDto>"
  }
}
```

## Analyzer test categories

### Roslyn

- class/type discovery
- method discovery
- direct method call resolution
- overload resolution
- interface implementation
- source location mapping

### ASP.NET Core

- controller route discovery
- action route discovery
- Minimal API route discovery
- route-to-method edges
- route-to-MediatR request edges

### Dependency Injection

- direct scoped/transient/singleton registrations
- constructor injection
- interface-to-implementation mapping
- duplicate registrations
- ambiguous scanning registration

### MediatR

- request discovery
- notification discovery
- handler discovery
- request-to-handler edge
- send/publish detection
- ambiguous runtime request construction

### EF Core

- DbContext discovery
- DbSet discovery
- context usage
- entity query edge
- `_context.Set<TEntity>()`

### Reflection

- `typeof` references
- `nameof` references
- assembly scanning
- Activator usage
- ambiguous runtime type creation

## CLI tests

CLI tests should verify:

- exit codes,
- output files,
- JSON parseability,
- command errors,
- `path` output for known fixtures,
- `explain` output for known nodes.

## MCP tests

MCP tests should use fixture graph files and verify:

- `get_node`,
- `get_neighbors`,
- `shortest_path`,
- `explain_path`,
- compact output shape for agent consumption.

## Benchmark tests

Benchmarks should not run on every pull request by default. They should run manually or on scheduled workflows.

Benchmark output should be published in `docs/performance.md` or a linked benchmark report before stable release.

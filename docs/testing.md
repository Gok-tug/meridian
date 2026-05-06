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

## Graph stability and cache-readiness tests

Before Meridian reuses analyzer output from cache, graph output must be stable enough to diff. Small deterministic guard tests should verify:

- `GraphBuilder` orders nodes, edges, and diagnostics deterministically,
- identical edge evidence is de-duplicated while distinct evidence is preserved,
- running the same analyzer fixture twice produces identical serialized graph JSON after line-ending normalization,
- cache design treats unknown or changed inputs as invalid instead of reusing stale facts.

Cache and incremental-analysis tests should eventually cover project file changes, source file changes, package/compiler option changes, analyzer version changes, graph schema changes, partial project invalidation, graph delta merge validation, and stale-cache prevention. Until cache exists, these remain design targets rather than required PR checks.

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

CLI tests should verify the process-level public contract:

- root help output,
- unknown command exit codes,
- missing argument exit codes,
- missing graph exit codes,
- output files,
- JSON parseability,
- command errors,
- `path` output for known fixtures,
- `explain` output for known nodes,
- `agent-summary` text sections, JSON parseability, and missing graph errors.

Scan smoke tests must also validate graph contents. A successful exit code with an empty graph is a failure.

Minimum graph-validity smoke assertions should include:

- node count above zero or a fixture-specific minimum,
- expected node kinds are present,
- expected relations such as `contains`, `calls`, `sends`, `publishes`, `handled_by`, `injects`, or `registered_as` are present when the fixture is meant to emit them,
- every edge source and target points to an existing node ID,
- required graph metadata such as schema version and generator is present.

Prefer process-level tests for CLI behavior because stdout, stderr, exit codes, and output files are the public contract.

## MCP tests

MCP tests should use fixture graph files and verify:

- `get_schema`,
- `reload_graph`,
- `get_node`,
- `get_neighbors`,
- `get_graph_statistics`,
- `get_agent_summary`,
- `shortest_path`,
- `explain_path`,
- compact output shape for agent consumption,
- truncation behavior,
- ambiguity candidates,
- stale graph notes.

Reload tests should verify:

- `reload_graph` updates visible schema and node counts after the configured file changes,
- queries after reload can see new graph facts,
- invalid JSON, duplicate node IDs, or dangling edge endpoints preserve the previous active graph,
- `get_schema` advertises `reload_graph`.

## External dogfood baselines

External repository scans should run manually or on scheduled validation, not in normal PR CI. Use `scripts/dogfood-baseline.ps1` for the pinned dogfood repositories and keep generated clones/outputs under ignored `.dogfood/` and `artifacts/` paths.

Dogfood scans should pass `--metrics` so baseline comparisons can use the same `metrics.json` fields for timings, graph counts, diagnostics, environment metadata, and Meridian version.

## Benchmark tests

Benchmarks should not run on every pull request by default. They should run manually or on scheduled workflows through `.github/workflows/benchmarks.yml`.

Run the isolated benchmark harness locally with:

```powershell
dotnet build "tests\Meridian.Benchmarks\Meridian.Benchmarks.csproj" -c Release
dotnet run --project "tests\Meridian.Benchmarks\Meridian.Benchmarks.csproj" -c Release -- benchmarks --quick
dotnet run --project "tests\Meridian.Benchmarks\Meridian.Benchmarks.csproj" -c Release -- payload-report --output "artifacts\benchmarks\mcp-payloads.json"
```

BenchmarkDotNet output and MCP payload reports are written under ignored `artifacts/benchmarks/` paths. Normal PR CI uses `Meridian.CI.slnf` for product/test validation and separately builds/formats `tests/Meridian.Benchmarks` without running benchmarks, while PR-safe payload-size guard tests remain in `Meridian.Mcp.Tests` because they use deterministic in-memory graphs.

Benchmark output should be published in `docs/performance.md` or a linked benchmark report before stable release.

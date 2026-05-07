# Performance

Meridian must be usable on real .NET solutions.

Roslyn workspace loading can dominate runtime, so Meridian should measure performance by phase instead of reporting only total time.

## Initial targets

Early benchmark targets:

| Solution size | Target |
| --- | --- |
| Small solution | under 30 seconds |
| Medium solution, 10-25 projects | under 2 minutes |
| Large solution, 50+ projects / 500k+ LOC | measured and published before stable release |

These are targets, not guarantees for early alpha builds.

## Current scan metrics

`meridian scan` can write a sidecar metrics file without changing the graph schema:

```bash
meridian scan MyApp.sln --output meridian-out --trust-project --metrics
```

Output:

```text
meridian-out/
  graph.json
  metrics.json
```

Current `metrics.json` shape:

```json
{
  "metrics_version": "0.1",
  "target": "MyApp.sln",
  "include_tests": false,
  "trusted_project": true,
  "started_utc": "2026-05-06T12:00:00.0000000+00:00",
  "total_ms": 12345,
  "analyze_ms": 12000,
  "export_ms": 120,
  "peak_working_set_mb": 512.34,
  "node_count": 947,
  "edge_count": 1655,
  "diagnostic_count": 0,
  "dotnet_version": "10.0.0",
  "os_description": "...",
  "meridian_version": "0.6.0-alpha.1"
}
```

These are CLI-level baseline metrics for repeatable dogfood and release validation. `peak_working_set_mb` is a best-effort process-level value from the current runtime/OS; compare it as trend data within the same runner family rather than as a cross-platform absolute. Metrics do not yet split Roslyn internals into workspace load, compilation, symbol indexing, or analyzer-specific timings.

## 0.5.0-alpha.1 dogfood baseline

The first repeatable baseline was generated on 2026-05-06 with:

```powershell
.\scripts\dogfood-baseline.ps1 -TrustProject
```

| Case | Total | Analyze | Export | Peak working set | Nodes | Edges | Diagnostics |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CleanArchitecture `.slnx` | 8.52s | 8.49s | 0.03s | 288.98 MB | 232 | 322 | 0 |
| eShopOnWeb solution | 11.29s | 11.23s | 0.06s | 324.07 MB | 943 | 1,652 | 5 |
| CrossMacro solution | 20.21s | 19.96s | 0.25s | 461.30 MB | 6,721 | 22,149 | 0 |

This baseline is a snapshot from one Windows machine and should be treated as trend data, not a formal benchmark guarantee.

## 0.5.0-alpha.2 benchmark and payload reports

`tests/Meridian.Benchmarks` contains an isolated BenchmarkDotNet harness for deterministic in-memory graph fixtures. It is included in `Meridian.sln` for local discovery but excluded from `Meridian.CI.slnf`; normal PR CI still builds and formats the benchmark project separately without executing benchmarks.

Run local benchmark smoke checks with:

```powershell
dotnet build "tests\Meridian.Benchmarks\Meridian.Benchmarks.csproj" -c Release
dotnet run --project "tests\Meridian.Benchmarks\Meridian.Benchmarks.csproj" -c Release -- benchmarks --quick
dotnet run --project "tests\Meridian.Benchmarks\Meridian.Benchmarks.csproj" -c Release -- payload-report --output "artifacts\benchmarks\mcp-payloads.json"
```

The benchmark workflow runs manually or weekly, uploads `artifacts/benchmarks`, and can optionally run `scripts/dogfood-baseline.ps1 -TrustProject` for external dogfood artifacts. Normal pull requests keep deterministic payload-size guard tests and benchmark harness compile/format checks, but do not run BenchmarkDotNet or network-dependent dogfood.

The MCP payload report records tool name, options, status, node/edge counts, truncation state, and UTF-8 serialized byte count for representative compact and evidence-included responses. Use it for trend comparison, not as an exact public contract snapshot.

## 0.5.0-alpha.3 cache-readiness design

`0.5.0-alpha.3` should prepare cache and incremental-analysis work without adding runtime cache behavior yet. The design target is a graph that is safe to diff and safe to reuse only when all relevant inputs are known to be unchanged.

Cache keys should include at least:

- project file content and imported build files when available,
- source file content hashes,
- relevant package references,
- compiler options and target framework,
- analyzer version and enabled analyzer set,
- graph schema version,
- Meridian generator version.

Invalidation must be conservative: if any relevant input is missing, unknown, or changed, Meridian should recompute the affected graph facts rather than reuse cached output. Planned cache hit rate and phase metrics should not be emitted in `metrics.json` until cache behavior exists and stale-graph prevention is tested.

## Planned phase metrics

Future scans should be able to report deeper phase timings:

```text
workspace_load_ms
compilation_ms
symbol_index_ms
analyzer_ms
graph_build_ms
export_ms
total_ms
peak_memory_mb
node_count
edge_count
cache_hit_rate
```

## Benchmark methodology

Benchmark reports should include:

- Meridian version,
- .NET SDK version,
- operating system,
- CPU,
- RAM,
- storage type when relevant,
- solution project count,
- approximate LOC,
- analyzer set,
- cold or warm run,
- cache state,
- total runtime,
- phase timings,
- peak memory.

## Cold vs warm runs

Meridian should distinguish:

- cold run: no Meridian cache,
- warm run: cache populated,
- partial run: only some projects changed.

## Caching strategy

Caching should be introduced before stable release. Before caching exists, Meridian should still preserve cache-friendly boundaries: deterministic node IDs, stable graph ordering, analyzer pass boundaries, and conservative input hashing assumptions.

Potential cache keys:

- project file hash,
- source file content hash,
- analyzer version,
- graph schema version,
- relevant package references,
- compiler options.

Cache invalidation must be conservative. A stale but fast graph is worse than a slower correct graph.

## Incremental analysis

Incremental analysis should eventually reuse unchanged project and analyzer results.

Potential approach:

```text
hash project inputs
  -> reuse symbol index if unchanged
  -> rerun affected analyzers
  -> merge graph deltas
  -> validate final graph
```

Incremental analysis should not be part of the first MVP unless it is simple and well-tested.

## Performance diagnostics

The CLI should eventually support:

```bash
meridian scan MyApp.sln --diagnostics performance
```

Output shape:

```text
Workspace load: 41.2s
Compilation:    18.5s
Symbol index:    6.1s
Analyzers:      22.4s
Graph build:     1.7s
Export:          0.4s
Total:          90.3s
Peak memory:  2.8 GB
```

## What not to optimize early

Do not optimize before correctness for:

- graph export formatting,
- rarely used visual outputs,
- speculative framework analyzers.

Prioritize:

- workspace load visibility,
- avoiding repeated semantic model work,
- deterministic output,
- analyzer-level benchmarks,
- memory usage on large solutions.

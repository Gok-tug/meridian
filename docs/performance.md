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

## Metrics to collect

Each scan should be able to report:

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

Caching should be introduced before stable release.

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

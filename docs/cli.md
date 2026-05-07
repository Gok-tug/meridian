# CLI

The Meridian CLI should expose graph generation and graph interrogation commands.

Commands should be deterministic, scriptable, and able to return either human-readable text or JSON.

## Command overview

```bash
meridian scan <solution-or-project>
meridian explain <node-or-symbol>
meridian path <source> <target>
meridian agent-summary [--graph <graph.json>]
meridian mcp --graph <graph.json>
```

## `meridian scan`

Builds a Meridian graph from a solution or project.

```bash
meridian scan MyApp.sln
```

Implemented options:

```bash
meridian scan MyApp.sln --output meridian-out
meridian scan MyApp.sln --include-tests
meridian scan MyApp.sln --trust-project
meridian scan MyApp.sln --metrics
```

`scan` uses Roslyn `MSBuildWorkspace`, which evaluates project and solution files. By default Meridian prints an MSBuild trust-boundary warning and records a warning diagnostic in `graph.json`. Pass `--trust-project` only for repositories you trust, or run scans inside an external sandbox.

Future options:

```bash
meridian scan MyApp.sln --format json
meridian scan MyApp.sln --analyzers aspnetcore,di,mediatr  # planned
meridian scan MyApp.sln --no-restore                       # planned
```

Default output:

```text
meridian-out/
  graph.json
```

With `--metrics`, `scan` also writes a sidecar metrics file next to the graph without changing the `graph.json` schema:

```text
meridian-out/
  graph.json
  metrics.json
```

`metrics.json` includes CLI-level timings, best-effort peak working set, graph counts, target path, trust/test flags, .NET/OS metadata, and the Meridian generator version. Treat memory values as same-runner trend data, not cross-platform absolutes.

`agent-summary` is an implemented derived view over `graph.json`. Future human-readable outputs such as `tree` and `report` are not emitted by `scan`.

ASP.NET Core preview support emits endpoint nodes for MVC route attributes, Minimal API `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch` calls, simple local `MapGroup` prefixes, FastEndpoints route verbs, and MinimalApi.Endpoint-style `AddRoute` methods when source patterns are statically visible.

EF Core preview support is static graph extraction for source `DbContext`, `DbSet<TEntity>`, `_context.Entities`, and `_context.Set<TEntity>()` patterns. Meridian emits entity access edges but does not reconstruct SQL, model provider behavior, migrations, or full LINQ expression semantics.

Reflection preview support covers static `typeof(T)` and `Activator.CreateInstance` targets. Runtime strings, runtime `Type` variables, Scrutor-style scanning, and assembly-load/type-scan inference are reported as limitations or diagnostics rather than guessed edges.

## `meridian explain`

Explains one graph node, route, type, method, or symbol.

```bash
meridian explain "GetOrderQuery"
```

Expected output shape:

```text
GetOrderQuery
Kind: mediatr_request
Source: Features/Orders/GetOrderQuery.cs:12

Incoming:
  none

Outgoing:
  GetOrderQuery --handled_by--> GetOrderQueryHandler
```

Implemented behavior reads an existing graph file, defaulting to `meridian-out/graph.json`:

```bash
meridian explain "GetOrderQuery" --graph meridian-out/graph.json
```

If a query matches multiple nodes at the same best score, Meridian reports the ambiguity and prints candidate labels, kinds, symbols, and node IDs instead of choosing one silently. Agents and scripts should not invent node IDs; they should copy exact IDs from `explain`, MCP `get_node`, or MCP `query_graph` results before calling traversal commands or tools:

```text
Node query 'CreateGroupAsync' is ambiguous. Use a more precise label, symbol, or node ID.
Candidates for 'CreateGroupAsync':
  WalletGroupService.CreateGroupAsync (method) score=60
    symbol: MyApp.WalletGroupService.CreateGroupAsync(...)
    id: method:MyApp:MyApp.WalletGroupService.CreateGroupAsync(...)
```

JSON mode is planned:

```bash
meridian explain "GetOrderQuery" --format json
```

## `meridian path`

Finds and explains an application-flow path between two nodes, symbols, routes, or labels. This traverses every graph edge present in `graph.json`, including endpoint `calls`, direct `calls`, `contains`, initial DI relations, mediator `sends`, `publishes`, and `handled_by`, EF Core `queries` and `writes`, and reflection `reflects` edges when they have been emitted.

```bash
meridian path "GET /orders/{id}" "OrderDbContext"
```

Current MediatR method-level paths can already traverse dispatch and handler edges:

```bash
meridian path "DispatchInlineRequest" "GetOrderQueryHandler"
```

Endpoint-aware output can traverse emitted endpoint, mediator, DI, and persistence edges:

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

The command should include confidence and evidence when verbose output is requested:

```bash
meridian path "GET /orders/{id}" "OrderDbContext" --verbose
```

Verbose output shape:

```text
GET /orders/{id}
  --sends--> GetOrderQuery
    confidence: EXTRACTED 1.00
    evidence: OrdersController.cs:42 IMediator.Send(new GetOrderQuery(...))

GetOrderQuery
  --handled_by--> GetOrderQueryHandler
    confidence: EXTRACTED 1.00
    evidence: GetOrderQueryHandler.cs:9 IRequestHandler<GetOrderQuery, OrderDto>
```

JSON mode:

```bash
meridian path "GET /orders/{id}" "OrderDbContext" --format json
```

JSON output shape:

```json
{
  "source": "GET /orders/{id}",
  "target": "OrderDbContext",
  "paths": [
    {
      "nodes": ["endpoint.orders.get_by_id", "type.GetOrderQuery", "type.GetOrderQueryHandler"],
      "edges": [
        {
          "relation": "sends",
          "confidence": "EXTRACTED",
          "confidence_score": 1.0
        }
      ]
    }
  ]
}
```

If multiple paths exist, Meridian should rank them by:

1. confidence,
2. shorter path length,
3. entrypoint relevance,
4. deterministic node ID ordering.

If either endpoint query is ambiguous, Meridian reports the source or target ambiguity and asks for a more precise label, symbol, or node ID before traversal. If both endpoints resolve but no path exists, Meridian returns a clear message:

```text
No path found from "GET /orders/{id}" to "OrderDbContext".
```

## `meridian agent-summary`

Summarizes an existing generated graph for graph-guided agent orientation.

```bash
meridian agent-summary --graph meridian-out/graph.json
meridian agent-summary --graph meridian-out/graph.json --budget compact
meridian agent-summary --graph meridian-out/graph.json --format json --max-items 3
```

Options:

- `--graph`, `-g`: input graph path. Defaults to `meridian-out/graph.json`.
- `--budget`: approximate response budget: `compact`, `standard`, or `detailed`.
- `--max-items`: maximum items per summary section.
- `--format`: `text` or `json`.

Text output includes graph metadata, counts, compact diagnostic groups when diagnostics exist, central nodes, likely extension points, conservative graph clusters when structure supports them, limitations, and suggested MCP queries. JSON output serializes the deterministic summary result directly, including grouped diagnostic statistics.

Budget modes control deterministic item caps, not exact tokenizer accounting. Central-node, extension-point, and cluster ranking uses distinct structural non-containment edges, while graph metadata and statistics still report raw edge counts from the loaded graph. Graph clusters are structure-only hints; verify source ownership before treating a cluster as an architectural subsystem. If source code changes, rerun `meridian scan` before trusting a derived summary.

## `meridian mcp`

Starts a local MCP server over an existing generated graph file.

```bash
meridian mcp --graph meridian-out/graph.json
```

The MCP server reads precomputed graph JSON and exposes typed tools such as `get_schema`, `get_graph_statistics`, `get_diagnostics`, `get_agent_summary`, `reload_graph`, `query_graph`, `get_node`, `get_neighbors`, `get_symbol_summary`, `plan_feature`, `shortest_path`, `explain_path`, `list_entrypoints`, and `find_flows_to_symbol`.

The graph is not updated live by `scan` alone. If source code changes, rerun `meridian scan`, then call `reload_graph` on the running MCP server or restart the MCP server before relying on tool results for the changed code.

See [agent-quickstart.md](agent-quickstart.md) for agent setup and [mcp.md](mcp.md) for tool contracts, truncation behavior, schema discovery, and limitations.

## Human-readable views

`agent-summary` is implemented as a deterministic orientation view derived from an existing `graph.json`.

Future CLI work may add additional graph views:

- `tree`: bounded hierarchical view from a node, type, namespace, or entrypoint
- `report`: Markdown overview for humans and agents

Derived views should not expand the graph schema or imply analyzer support that has not emitted facts.

## Exit codes

Recommended exit codes:

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | Analysis or query failed |
| 2 | Invalid command or arguments |
| 3 | Solution/project could not be loaded |
| 4 | Graph validation failed |
| 5 | No matching node or symbol found, or node query is ambiguous |

## Output stability

Machine-readable output should be deterministic. Human-readable output can improve over time, but examples in docs should be updated whenever command output changes.

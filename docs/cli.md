# CLI

The Meridian CLI should expose graph generation and graph interrogation commands.

Commands should be deterministic, scriptable, and able to return either human-readable text or JSON.

## Command overview

```bash
meridian scan <solution-or-project>
meridian explain <node-or-symbol>
meridian path <source> <target>
meridian mcp --graph <graph.json>
```

## `meridian scan`

Builds a Meridian graph from a solution or project.

```bash
meridian scan MyApp.sln
```

Planned options:

```bash
meridian scan MyApp.sln --output meridian-out
meridian scan MyApp.sln --format json
meridian scan MyApp.sln --include-tests
meridian scan MyApp.sln --analyzers aspnetcore,di,mediatr  # planned
meridian scan MyApp.sln --no-restore                       # planned
```

Default output:

```text
meridian-out/
  graph.json
  report.md
```

Initial `0.1.0-alpha.1` may only support `graph.json`.

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

Current behavior reads an existing graph file, defaulting to `meridian-out/graph.json`:

```bash
meridian explain "GetOrderQuery" --graph meridian-out/graph.json
```

If a query matches multiple nodes at the same best score, Meridian reports the ambiguity and prints candidate labels, kinds, symbols, and node IDs instead of choosing one silently:

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

Finds and explains an application-flow path between two nodes, symbols, routes, or labels. In the current prototype, this traverses every graph edge present in `graph.json`, including direct `calls`, `contains`, initial DI relations, and MediatR `sends`, `publishes`, and `handled_by` edges when they have been emitted.

```bash
meridian path "GET /orders/{id}" "OrderDbContext"
```

Current MediatR method-level paths can already traverse dispatch and handler edges:

```bash
meridian path "DispatchInlineRequest" "GetOrderQueryHandler"
```

Framework-aware expected output once planned endpoint and EF Core analyzers exist:

```text
GET /orders/{id}
  --sends--> GetOrderQuery
  --handled_by--> GetOrderQueryHandler
  --injects--> IOrderRepository
  --implemented_by--> EfOrderRepository
  --uses--> OrderDbContext
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

## `meridian mcp`

Starts a local MCP server over an existing generated graph file.

```bash
meridian mcp --graph meridian-out/graph.json
```

The MCP server reads precomputed graph JSON and exposes typed tools such as `get_schema`, `query_graph`, `get_node`, `get_neighbors`, `shortest_path`, `explain_path`, `list_entrypoints`, and `find_flows_to_symbol`.

The graph is not updated live. If source code changes, rerun `meridian scan` before relying on MCP tool results for the changed code.

See [mcp.md](mcp.md) for tool contracts, truncation behavior, schema discovery, and limitations.

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

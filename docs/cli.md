# CLI

The Meridian CLI should expose graph generation and graph interrogation commands.

Commands should be deterministic, scriptable, and able to return either human-readable text or JSON.

## Command overview

```bash
meridian scan <solution-or-project>
meridian explain <node-or-symbol>
meridian path <source> <target>
meridian query <question>
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

Initial `0.1.0-alpha.1` behavior reads an existing graph file, defaulting to `meridian-out/graph.json`:

```bash
meridian explain "GetOrderQuery" --graph meridian-out/graph.json
```

JSON mode is planned:

```bash
meridian explain "GetOrderQuery" --format json
```

## `meridian path`

Finds and explains an application-flow path between two nodes, symbols, routes, or labels. In the current prototype, this traverses every graph edge present in `graph.json`, including direct `calls`, `contains`, initial DI relations, and MediatR declaration `handled_by` edges when they have been emitted.

```bash
meridian path "GET /orders/{id}" "OrderDbContext"
```

Framework-aware expected output once planned endpoint, MediatR, and EF Core analyzers exist:

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

If no path is found, Meridian should return a clear message:

```text
No path found from "GET /orders/{id}" to "OrderDbContext".
```

## `meridian query`

Runs a graph query. Early versions may use structured graph search only. Later versions may support natural-language query through MCP or agent integration.

```bash
meridian query "which endpoints can reach OrderDbContext?"
```

Expected answer shape:

```text
3 entrypoints can reach OrderDbContext:

- GET /orders/{id}
- POST /orders
- DELETE /orders/{id}
```

## Exit codes

Recommended exit codes:

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | Analysis or query failed |
| 2 | Invalid command or arguments |
| 3 | Solution/project could not be loaded |
| 4 | Graph validation failed |
| 5 | No matching node or symbol found |

## Output stability

Machine-readable output should be deterministic. Human-readable output can improve over time, but examples in docs should be updated whenever command output changes.

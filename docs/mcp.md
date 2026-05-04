# MCP Server

MCP support is a core Meridian use case, not a distant add-on.

The first MCP preview is planned for `0.3.0-alpha.1`.

## Purpose

Meridian generates a compact graph that AI agents can query instead of repeatedly searching the repository from scratch.

An MCP server should let agents ask questions such as:

- Which endpoints can reach this DbContext?
- What handles this MediatR request?
- What services are injected into this handler?
- Is there a path from this route to this repository?
- Which graph facts are inferred or ambiguous?

## Initial tools

### `query_graph`

Runs a graph query and returns compact results.

Input shape:

```json
{
  "query": "which endpoints can reach OrderDbContext?"
}
```

### `get_node`

Returns one node by ID, label, or symbol.

Input shape:

```json
{
  "id_or_label": "GetOrderQuery"
}
```

### `get_neighbors`

Returns nearby graph nodes.

Input shape:

```json
{
  "id_or_label": "GetOrderQueryHandler",
  "direction": "both",
  "depth": 1
}
```

### `shortest_path`

Returns graph paths between two nodes.

Input shape:

```json
{
  "source": "GET /orders/{id}",
  "target": "OrderDbContext"
}
```

### `explain_path`

Returns a human-readable path with relation labels, confidence, and evidence.

Input shape:

```json
{
  "source": "GET /orders/{id}",
  "target": "OrderDbContext",
  "include_evidence": true
}
```

### `list_entrypoints`

Lists discovered application entrypoints.

Examples:

- HTTP endpoints,
- controller actions,
- Minimal API routes,
- background job methods in future analyzers,
- message consumers in future analyzers.

### `find_flows_to_symbol`

Finds entrypoints or upstream nodes that can reach a target symbol.

Input shape:

```json
{
  "target": "OrderDbContext",
  "max_depth": 8
}
```

## Response style

MCP responses should be compact, source-linked, and confidence-aware.

Example:

```text
GET /orders/{id} reaches OrderDbContext through 5 edges.

1. GET /orders/{id} --sends--> GetOrderQuery [EXTRACTED]
   OrdersController.cs:42 IMediator.Send(new GetOrderQuery(...))
2. GetOrderQuery --handled_by--> GetOrderQueryHandler [EXTRACTED]
   GetOrderQueryHandler.cs:8 IRequestHandler<GetOrderQuery, OrderDto>
```

## Data source

The initial MCP server should read generated `graph.json` files instead of analyzing the solution live.

This keeps MCP fast and predictable:

```bash
meridian scan MyApp.sln --output meridian-out
meridian mcp --graph meridian-out/graph.json
```

## Security

The MCP server should:

- read only the configured graph file or output directory,
- not execute analyzed application code,
- not expose source file contents unless explicitly configured,
- avoid returning huge graph payloads by default,
- include confidence and evidence so agents do not overstate uncertain links.

# MCP Server

MCP support is a core Meridian use case.

The first MCP preview is part of `0.3.0-alpha.1` and reads generated `graph.json` files. It does not load or analyze the solution live.

## Purpose

Meridian generates a compact graph that AI agents can query instead of repeatedly searching the repository from scratch.

The MCP server is optimized for agent use:

- answers over precomputed JSON graph data,
- uses strongly typed tool parameters instead of a custom query DSL,
- exposes schema discovery so agents can see available node kinds and relations,
- caps broad result sets and marks truncation explicitly,
- preserves ambiguity handling instead of silently picking a node,
- reminds callers that graph data is stale until `meridian scan` is rerun.

## Run

```bash
meridian scan MyApp.sln --output meridian-out
meridian mcp --graph meridian-out/graph.json
```

The MCP server communicates over stdio for local MCP clients.

## Stale graph rule

Every tool should be understood with this rule:

```text
This graph is precomputed. If source code changes, MCP results will not reflect those changes until meridian scan is run again.
```

Agents should rerun `meridian scan` after code edits before relying on graph queries for the changed code.

## Initial tools

### `get_schema`

Returns graph metadata, tool names, node kinds, and relation names.

Use this first when an agent does not know which node kinds or relations are available.

Output includes:

- graph schema version,
- generator version,
- node count,
- edge count,
- diagnostic count,
- node kinds present in the loaded graph,
- relations present in the loaded graph,
- known Meridian node-kind constants,
- known Meridian relation constants,
- stale graph note.

### `query_graph`

Runs a typed graph filter. It does not accept a custom string query language.

Input parameters:

```json
{
  "text": "GetOrderQuery",
  "nodeKind": "mediatr_request",
  "relation": "handled_by",
  "direction": "Outgoing",
  "source": "GetOrderQuery",
  "target": "GetOrderQueryHandler",
  "maxResults": 50
}
```

All fields are optional. Agents should combine filters instead of asking broad natural-language questions.

Unsupported natural-language text returns a limitation with suggested typed parameters rather than trying to guess intent.

### `get_node`

Returns one node by ID, label, or symbol.

Input:

```json
{
  "idOrLabel": "GetOrderQuery"
}
```

If the query is ambiguous, the response contains candidates with labels, kinds, symbols, IDs, and scores. The agent should retry with an exact node ID or more precise symbol.

### `get_neighbors`

Returns nearby graph nodes and edges.

Input:

```json
{
  "idOrLabel": "GetOrderQueryHandler",
  "direction": "Both",
  "depth": 1,
  "relation": "injects",
  "maxResults": 50
}
```

`direction` values:

- `Incoming`
- `Outgoing`
- `Both`

The server caps both traversal depth and horizontal result count.

When a cap is reached, responses include:

```text
TRUNCATED: Limit of 50 results reached. Use get_node or narrower query_graph filters to drill down.
```

### `shortest_path`

Returns the shortest directed graph path between two resolved nodes.

Input:

```json
{
  "source": "OrderController.Get",
  "target": "OrderDbContext"
}
```

If either endpoint is ambiguous, the response returns candidates instead of traversing from a guessed node.

### `explain_path`

Returns a path with relation, confidence, and optional evidence.

Input:

```json
{
  "source": "OrderController.Get",
  "target": "OrderDbContext",
  "includeEvidence": true
}
```

Evidence includes file, line, symbol, and reason when those fields are present in the graph.

### `list_entrypoints`

Lists emitted entrypoint nodes.

Current analyzers do not yet emit ASP.NET Core endpoint nodes, so many early graphs return a limitation:

```text
Current Meridian analyzers do not yet emit ASP.NET Core endpoint nodes.
```

This is expected until the ASP.NET Core analyzer milestone.

### `find_flows_to_symbol`

Reverse-traverses existing graph edges to find upstream nodes that can reach a target.

Input:

```json
{
  "target": "OrderDbContext",
  "maxDepth": 8,
  "maxResults": 50
}
```

If endpoint nodes exist, agents can use them as application entrypoints. If not, the tool returns upstream nodes plus the endpoint analyzer limitation instead of inventing HTTP routes.

## Result limits

Tools that return node or edge arrays are capped by default. The preview uses conservative limits to avoid poisoning the agent context window with huge JSON payloads.

Agents should respond to truncation by narrowing filters, lowering depth, or querying exact node IDs.

## Data source

The initial MCP server reads generated graph JSON files only:

```bash
meridian mcp --graph meridian-out/graph.json
```

It does not execute analyzed application code and does not run Roslyn/MSBuild live during MCP tool calls.

## Security

The MCP server should:

- read only the configured graph file,
- not execute analyzed application code,
- not expose source file contents,
- avoid returning huge graph payloads by default,
- include confidence and evidence so agents do not overstate uncertain links,
- surface limitations when analyzers have not emitted endpoint, EF Core, reflection, or native interop facts.

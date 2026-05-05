# MCP Server

MCP support is a core Meridian use case.

The first MCP preview is part of `0.3.0-alpha.1` and reads generated `graph.json` files. `0.3.0-alpha.2` adds explicit `reload_graph` support for refreshing a running MCP server after the graph file changes. MCP does not load or analyze the solution live.

## Purpose

Meridian generates a compact graph that AI agents can query instead of repeatedly searching the repository from scratch.

The MCP server is optimized for agent use:

- answers over precomputed JSON graph data,
- uses strongly typed tool parameters instead of a custom query DSL,
- exposes schema discovery so agents can see available node kinds and relations,
- caps broad result sets and marks truncation explicitly,
- preserves ambiguity handling instead of silently picking a node,
- reminds callers that graph data is stale until `meridian scan` is rerun and the running MCP server is reloaded or restarted.

## Run

```bash
meridian scan MyApp.sln --output meridian-out
meridian mcp --graph meridian-out/graph.json
```

The MCP server communicates over stdio for local MCP clients.

## Stale graph rule

Every tool should be understood with this rule:

```text
This graph is precomputed. If source code changes, MCP results will not reflect those changes until meridian scan is run again and the running MCP server is reloaded with reload_graph or restarted.
```

`meridian scan` regenerates `graph.json` on disk. A running MCP server keeps its current in-memory graph until `reload_graph` succeeds or the MCP process restarts.

Freshness workflow after source edits:

```text
edit source -> meridian scan -> reload_graph -> query again
```

See [agent-playbook.md](agent-playbook.md) for agent workflow guidance.

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
- node-kind and relation counts,
- usage hints for compact responses, evidence opt-in, relation exclusions, and graph-absence interpretation,
- stale graph note.

### `reload_graph`

Rereads the configured graph file into the running MCP server.

Use this after rerunning `meridian scan` while the MCP server is still connected.

Input: none.

Output includes:

- status,
- graph path,
- previous node and edge counts,
- current node and edge counts,
- generator version,
- load timestamp,
- graph file timestamp,
- `previousGraphPreserved` flag,
- failure message when reload fails.

If reload fails, the previous graph remains active.

`reload_graph` does not run Roslyn/MSBuild, does not execute application code, and does not choose another graph file. It only rereads the file configured with `meridian mcp --graph`.

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
  "maxResults": 50,
  "includeEvidence": false,
  "excludeRelations": ["contains"]
}
```

All fields are optional. Agents should combine filters instead of asking broad natural-language questions.

Bulk edge responses omit evidence by default to protect the agent context window. Set `includeEvidence` to `true` when the file, line, symbol, and reason are needed. Use `excludeRelations` to keep structural edges such as `contains` from consuming broad result caps.

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

Agents must not invent node IDs. If the exact ID is unknown, use `query_graph` or returned candidates to discover it, then pass the exact returned ID to traversal tools.

### `get_neighbors`

Returns nearby graph nodes and edges.

Input:

```json
{
  "idOrLabel": "GetOrderQueryHandler",
  "direction": "Both",
  "depth": 1,
  "relation": "injects",
  "maxResults": 50,
  "includeEvidence": false,
  "excludeRelations": ["contains"]
}
```

`direction` values:

- `Incoming`
- `Outgoing`
- `Both`

The server caps both traversal depth and horizontal result count. Excluded relations are filtered before traversal and capping, so `excludeRelations: ["contains"]` keeps declaration-containment noise from hiding behavioral edges.

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
  "maxResults": 50,
  "includeEvidence": false,
  "excludeRelations": ["contains"]
}
```

If endpoint nodes exist, agents can use them as application entrypoints. If not, the tool returns upstream nodes plus the endpoint analyzer limitation instead of inventing HTTP routes.

## Result limits

Tools that return node or edge arrays are capped by default. The preview uses conservative limits to avoid poisoning the agent context window with huge JSON payloads.

Agents should respond to truncation by narrowing filters, lowering depth, or querying exact node IDs.

## Interpreting absence

A missing node, edge, or path means the fact is not recorded in the currently loaded precomputed Meridian graph. It is not proof that the relationship is absent from source code. If source files changed, regenerate the graph with `meridian scan` and call `reload_graph` before drawing conclusions.

## Data source

The MCP server reads generated graph JSON files only:

```bash
meridian mcp --graph meridian-out/graph.json
```

It does not execute analyzed application code and does not run Roslyn/MSBuild live during MCP tool calls.

When `graph.json` changes on disk, the running MCP server does not observe that change until `reload_graph` succeeds or the server restarts.

## Hooks and watch mode

Automatic watch/hot-reload is not currently implemented.

External hooks may run `meridian scan`, but scan alone only updates `graph.json` on disk. Any automation that changes the graph file must also call `reload_graph` or restart the MCP server before agents rely on the new data.

Built-in watch support should remain opt-in and should wait for a clearer cache/incremental analysis design.

## Security

The MCP server should:

- read only the configured graph file,
- reload only the configured graph file when `reload_graph` is called,
- not execute analyzed application code,
- not expose source file contents,
- avoid returning huge graph payloads by default,
- include confidence by default and make detailed evidence available on request so agents do not overstate uncertain links,
- surface endpoint and graph-absence limitations when facts have not been emitted, and avoid inventing EF Core, reflection, or native interop facts until those analyzers exist.

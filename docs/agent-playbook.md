# Agent Playbook

This playbook describes how MCP-enabled agents should use Meridian safely.

Meridian answers from a precomputed `graph.json`. It does not analyze source code live during MCP tool calls.

## Standard workflow

1. Generate or refresh the graph:

   ```bash
   meridian scan MyApp.sln --output meridian-out
   ```

2. Start or connect to MCP:

   ```bash
   meridian mcp --graph meridian-out/graph.json
   ```

3. Call `get_schema` before using graph tools.

4. Use typed tools. Do not send custom query languages or broad natural-language questions to `query_graph`.

5. Resolve nodes before traversal. If you do not know the exact node ID, use `query_graph` or `get_node` first.

6. Use exact returned node IDs for `get_neighbors`, `shortest_path`, and `explain_path` when possible.

7. If a response is truncated, narrow the query instead of asking for a larger graph dump.

8. Cite confidence and evidence when making claims about code flow.

9. State unsupported analyzer limitations honestly.

## Freshness workflow after edits

If source code changes, `meridian scan` updates the graph file on disk. A running MCP server does not automatically reread that file unless it is explicitly reloaded.

After editing source code:

```text
edit source -> meridian scan -> reload_graph -> query again
```

If `reload_graph` is unavailable, restart the MCP server after rerunning `meridian scan`.

Do not trust MCP results for changed code until the graph has been regenerated and the running MCP server has either reloaded or restarted.

## Node ID strategy

Never invent Meridian node IDs.

A C# symbol name in source code is not enough to infer the exact graph node ID. Meridian IDs include analyzer-specific structure and assembly/symbol information.

Use this strategy:

1. Search by text:

   ```json
   {
     "text": "OrderService"
   }
   ```

2. Inspect returned nodes or candidates.

3. Copy the exact returned `id`.

4. Use that exact ID in traversal tools:

   ```json
   {
     "idOrLabel": "method:MyApp:MyApp.Orders.OrderService.CreateAsync(...)"
   }
   ```

If `get_node` returns `ambiguous`, retry with a more precise symbol, label, or exact candidate ID. Do not choose the first candidate silently.

## Tool usage guidance

### `get_schema`

Use first. It tells you which tools, node kinds, and relations are available in the current graph.

### `query_graph`

Use typed filters:

```json
{
  "text": "CreateOrder",
  "nodeKind": "method",
  "relation": "calls",
  "direction": "Outgoing",
  "maxResults": 25
}
```

Do not pass a natural-language question as the only input.

### `get_node`

Use for exact resolution by ID, label, or symbol. Treat ambiguity as a request to narrow the query.

### `get_neighbors`

Use after resolving a node. Prefer exact node IDs. Keep depth and result limits small.

### `shortest_path` and `explain_path`

Use exact source and target IDs when possible. If either endpoint is ambiguous, resolve it first with `query_graph` or `get_node`.

### `reload_graph`

Use after `meridian scan` when the MCP server is already running. It rereads the configured graph file into memory. It does not run Roslyn/MSBuild and does not execute application code.

If reload fails, keep using the previous graph only if the response says the previous graph was preserved, and tell the user the graph is stale.

## Unsupported or limited facts

Current alpha builds do not yet emit full ASP.NET Core endpoint flow, EF Core flow, reflection resolution, broad DI dataflow, or native/Rust interop facts.

When a Meridian tool reports a limitation, do not fill the gap by guessing. Use normal code inspection separately and label those findings as outside Meridian's graph facts.

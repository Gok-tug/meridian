# Agent Playbook

This playbook describes how MCP-enabled agents should use Meridian safely.

Meridian answers from a precomputed `graph.json`. It does not analyze source code live during MCP tool calls.

## When to use Meridian MCP first

Use Meridian MCP first when a task needs graph-shaped .NET orientation rather than a precise single-file edit:

- broad codebase analysis in an unfamiliar repository
- feature placement or impact analysis
- endpoint-to-handler, DI, MediatR/Mediator, EF Core, reflection, or dynamic-wiring flow tracing
- shortest-path or dependency questions between symbols, routes, services, handlers, and persistence types
- questions like “where should this change go?” or “what code path owns this behavior?”

Use source tools first when the user gives an exact file and small edit, such as fixing a typo, changing one known method, or making a syntax-level adjustment. Also use source tools directly when MCP is unavailable, the graph is stale or missing, or the domain is outside current analyzer coverage.

The intended loop is:

```text
MCP orient -> resolve exact node IDs -> source verify -> edit -> user-initiated rescan -> reload_graph -> re-query if needed
```

Do not silently run `meridian scan` just because fresher graph context would be useful. If the graph is stale or missing, tell the user and ask before rescanning unless the user already requested a verification workflow. After a user-initiated rescan, call `reload_graph` or restart MCP before trusting graph results for changed code.

Meridian guides where to look; it does not replace source verification before edits.

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

4. For broad orientation, call `get_agent_summary` or `get_graph_statistics` before reading source or traversing neighbors. If diagnostics drive the question, use bounded `get_diagnostics` instead of reading all raw diagnostics from `graph.json`.

5. Use typed tools. Do not send custom query languages or broad natural-language questions to `query_graph`.

6. Resolve nodes before traversal. If you do not know the exact node ID, use `query_graph` or `get_node` first.

7. Use `get_symbol_summary` before broad neighbor traversal when you need compact context for one symbol.

8. Use exact returned node IDs for `get_neighbors`, `shortest_path`, and `explain_path` when possible.

9. If a response is truncated, narrow the query instead of asking for a larger graph dump.

10. Cite confidence and evidence when making claims about code flow.

11. State unsupported analyzer limitations honestly.

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

### `get_graph_statistics`

Use after `get_schema` when you need compact counts, confidence breakdowns, exact top diagnostics, grouped diagnostic summaries, limitations, and suggested next tools without edge payloads.

### `get_diagnostics`

Use when diagnostic summaries show relevant volume and you need bounded raw diagnostic messages or source locations. Filter by `id`, `severity`, `sourceFile`, or `text`, keep `maxResults` small, and leave `includeGroups` enabled unless you only need raw rows.

### `get_agent_summary`

Use before broad source reading or neighbor traversal. Start with `budget: "compact"` for orientation, then follow suggested `get_symbol_summary`, `plan_feature`, or exact node queries.

Treat central nodes and clusters as graph-derived navigation hints, not proof of architectural ownership or source-code absence. Summary ranking uses distinct structural non-containment edges so repeated evidence for the same source/relation/target does not dominate orientation, while graph statistics still report raw loaded edge counts. In UI-heavy graphs, `binds_to` remains visible in counts and queryable, but high-volume summaries guide UI questions to `relation:"binds_to"` and non-UI traversal to `excludeRelations:["contains","binds_to"]`.

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

Do not pass a natural-language question as the only input. Read the response `summary` metadata to understand returned node kinds, relation mix, confidence mix, and caps before widening a query.

### `get_node`

Use for exact resolution by ID, label, or symbol. Treat ambiguity as a request to narrow the query.

### `get_neighbors`

Use after resolving a node. Prefer exact node IDs. Keep depth and result limits small. For service or type nodes, prefer `excludeRelations: ["contains"]` unless declaration containment is what you need. In UI-heavy graphs, use `excludeRelations: ["contains", "binds_to"]` for non-UI architecture traversal.

### `get_symbol_summary`

Use after resolving a node when you need source location, relation counts, contained members, interface/DI links, and suggested follow-up queries without loading full neighbor edge arrays.

### `plan_feature`

Use when the user asks where to add a new concept that may not exist yet. Provide the goal, known seed symbols, and optional domain terms. Treat the result as ranked graph navigation, not an implementation plan.

If a new term is absent, do not search only for that term. Start from existing abstractions and extension points such as modes, strategies, policies, factories, registries, resolvers, selectors, executors, orchestrators, dispatchers, and handlers. Use grep/read only for missing domain vocabulary or source details not represented in the graph.

### `shortest_path` and `explain_path`

Use exact source and target IDs when possible. If either endpoint is ambiguous, resolve it first with `query_graph` or `get_node`.

### `reload_graph`

Use after `meridian scan` when the MCP server is already running. It rereads the configured graph file into memory. It does not run Roslyn/MSBuild and does not execute application code.

If reload fails, keep using the previous graph only if the response says the previous graph was preserved, and tell the user the graph is stale.

## Supported and limited facts

Current alpha builds emit:

- direct method calls, type/member containment, enum/property/field nodes, and interface implementation edges
- ordinary-method `reads`, `writes`, and `uses` edges for directly resolved source members and enum references
- method-level `branches_on` and `switches_on` preview edges for directly resolved simple conditions
- CommunityToolkit.Mvvm `[ObservableProperty]` and `[RelayCommand]` generated-member preview nodes and `generated_from` edges
- typed Avalonia AXAML static binding preview edges with `binds_to` for simple bindings, generated Toolkit members, and static template scopes; use `relation:"binds_to"` for UI binding questions
- constructor injection and generic DI registration edges, including narrow direct `new` and direct `GetRequiredService<TImplementation>()` factory lambdas
- MediatR declaration, `sends`, `publishes`, and `handled_by` edges
- EF Core `DbContext` containment, `queries`, and `writes` edges for statically resolved entity types
- static reflection edges for `typeof(T)`, `Activator.CreateInstance<T>()`, and `Activator.CreateInstance(typeof(T))`
- ASP.NET Core endpoint nodes and endpoint-to-handler flow for MVC attributes, Minimal API `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch`, simple local `MapGroup` prefixes, FastEndpoints route verbs, and MinimalApi.Endpoint-style route registration

Current alpha builds do not yet emit:

- broad DI dataflow beyond direct generic registrations and narrow factory aliases
- CLI/runtime command routing through resolver dictionaries, delegates, or factories
- native/Rust interop boundary detection
- XAML runtime binding behavior beyond conservative typed Avalonia AXAML scopes
- full CommunityToolkit.Mvvm source-generator behavior beyond the narrow generated-member preview

When a Meridian tool reports a limitation, do not fill the gap by guessing. Use normal code inspection separately and label those findings as outside Meridian's graph facts.

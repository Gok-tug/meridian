---
name: meridian
description: Use Meridian MCP for broad .NET codebase analysis, feature placement, impact analysis, and framework-mediated flow tracing before targeted source inspection.
---

# Meridian MCP workflow

Use this skill when the user asks for broad .NET codebase analysis, feature placement, impact analysis, dependency/flow tracing, endpoint-to-handler paths, DI registration/injection paths, MediatR/Mediator flow, EF Core persistence flow, reflection/dynamic wiring, or where a change should be made.

Do not use this skill for small single-file fixes such as “fix this typo in `Foo.cs`” or “rename this known local variable” unless the user asks for graph context.

Follow the canonical decision rules in `docs/agent-playbook.md`.

## MCP-first loop

1. If Meridian MCP tools are available and the graph appears fresh, call `get_schema` first.
2. For broad orientation, call `get_agent_summary` with a compact budget or `get_graph_statistics`.
3. Use `get_diagnostics` when grouped diagnostic summaries need bounded raw detail.
4. Resolve ambiguous symbols with `query_graph` or `get_node`; never invent Meridian node IDs.
5. Use `get_symbol_summary`, `get_neighbors`, `shortest_path`, `explain_path`, `list_entrypoints`, `find_flows_to_symbol`, or `plan_feature` as appropriate.
6. Verify exact source with normal source-reading/search tools before editing.

## Stale graph rule

Meridian MCP answers from a precomputed `graph.json`, not live source. If source changed or the graph is missing/stale, tell the user. Do not silently run `meridian scan`; ask the user before rescanning unless they already requested a verification workflow. After a user-initiated rescan, use `reload_graph` or restart MCP before trusting changed-code graph results.

Missing graph facts are not proof that the source code lacks the behavior. Use source inspection for unsupported analyzer domains or absent graph facts.

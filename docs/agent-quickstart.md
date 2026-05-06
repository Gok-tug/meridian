# Agent Quickstart

This guide shows how to connect Meridian to coding agents so broad .NET analysis starts from the Meridian graph before targeted source inspection.

Meridian MCP answers from a precomputed `graph.json`; it does not analyze source code live during MCP calls. Use the graph for orientation, then verify exact source before editing.

## 1. Install Meridian

Install the prerelease .NET global tool:

```bash
dotnet tool install --global meridian --prerelease
```

Update an existing installation:

```bash
dotnet tool update --global meridian --prerelease
```

The tool command is `meridian`.

## 2. Generate a graph

Run Meridian against a solution or project:

```bash
meridian scan MyApp.sln --output meridian-out
```

This writes `meridian-out/graph.json`.

## 3. Verify the graph before wiring MCP

Check that the graph exists and is useful before connecting an agent:

```bash
meridian agent-summary --graph meridian-out/graph.json --budget compact
```

This gives a compact orientation view and catches missing or stale graph paths early.

## 4. Configure Claude Code MCP

Claude Code can add Meridian as a project-scoped stdio MCP server:

```bash
claude mcp add --transport stdio --scope project meridian -- meridian mcp --graph meridian-out/graph.json
```

Current Claude Code syntax expects MCP options before the server name, and `--` before the server command.

Equivalent project `.mcp.json` template:

```json
{
  "mcpServers": {
    "meridian": {
      "command": "meridian",
      "args": ["mcp", "--graph", "meridian-out/graph.json"],
      "env": {}
    }
  }
}
```

Do not commit `.mcp.json` unless the team wants a shared project MCP server. Project-scoped MCP config is shared and Claude may ask users to approve it. Use local or user MCP scope for personal setup or secret-bearing configuration.

## 5. Add the Claude Code skill

Meridian includes a minimal project skill at:

```text
.claude/skills/meridian/SKILL.md
```

To use it in another repository, copy that directory into the target repository. For a personal skill available across projects, copy it to:

```text
%USERPROFILE%\.claude\skills\meridian\SKILL.md
```

Claude Code uses the skill `description` to decide when to load it. The skill is a preview workflow wrapper over Meridian MCP; MCP remains the capability layer. Legacy `.claude/commands/` files may still work in some setups, but new Meridian workflow guidance should use skills.

## 6. Configure OpenCode-like or other MCP clients

Claude Code skills are Claude-specific. OpenCode-like tools can still use Meridian if they support stdio MCP.

Use the same server command:

```bash
meridian mcp --graph meridian-out/graph.json
```

If the client supports project MCP config, adapt the `.mcp.json` command and args shape above. If the client reads generic instruction files, point it to `docs/agent-playbook.md` or use the root `AGENTS.md` guidance.

Do not assume Claude Code skill auto-loading applies to non-Claude clients.

## 7. Expected agent behavior

A broad prompt should trigger MCP-first orientation:

```text
Analyze where to add a new order export feature. Use Meridian if available, then verify source before editing.
```

Expected behavior:

1. Call `get_schema`.
2. Call `get_agent_summary` or `get_graph_statistics`.
3. Resolve relevant nodes with typed MCP tools.
4. Read exact source files to verify before editing.
5. If source changes and the user wants graph validation, rerun `meridian scan` and call `reload_graph` or restart MCP.

A narrow prompt should not need Meridian first:

```text
Fix the typo in src/Orders/OrderFormatter.cs.
```

Use source tools directly for precise single-file edits unless the user asks for graph context.

## Important caveats

- `CLAUDE.md`, `AGENTS.md`, and skills guide agent behavior; they do not guarantee the model will always choose MCP.
- Meridian graph absence is not proof of source absence.
- MCP results are stale after source edits until `meridian scan` runs and the MCP server reloads or restarts.
- See [agent-playbook.md](agent-playbook.md) for canonical decision rules.
- See [mcp.md](mcp.md) for MCP tool contracts.

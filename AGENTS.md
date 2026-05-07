# Agent instructions

Use Meridian MCP first for broad graph-worthy .NET questions when the client exposes MCP tools and the graph is fresh:

- codebase orientation
- feature placement and impact analysis
- endpoint-to-handler, DI, MediatR/Mediator, EF Core, reflection, or dynamic-wiring flow tracing
- path/dependency questions between routes, symbols, services, handlers, and persistence types

Use direct source inspection first for exact single-file fixes, syntax-only changes, stale/missing graph situations, unsupported analyzer domains, and final verification before edits.

If MCP is unavailable, use the CLI summary as a fallback:

```bash
meridian agent-summary --graph meridian-out/graph.json --budget compact
```

For unreleased Meridian CLI changes in this repository, run from source:

```powershell
dotnet run --project "src\Meridian.Cli\Meridian.Cli.csproj" -c Release -- <command>
```

Canonical workflow docs:

- `docs/agent-playbook.md`
- `docs/agent-quickstart.md`
- `docs/mcp.md`

Meridian MCP answers from a precomputed graph. If source changes, graph answers are stale until the user reruns `meridian scan` and the MCP server reloads or restarts.

Versioning and changelog policy:

- Do not bump `src/Meridian.Cli/Meridian.Cli.csproj` version unless the user explicitly asks for release preparation or a version bump.
- For ordinary unreleased work, add changelog entries under `## Unreleased`; do not open a new release section proactively.
- Only move `Unreleased` entries into `## <version> — <title>` during explicit release preparation, and the section version must match the package version being released.
- `CHANGELOG.md` is completed release history; `ROADMAP.md` is milestone planning. Do not duplicate roadmap plans into changelog release sections.

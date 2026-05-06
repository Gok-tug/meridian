# Meridian Copilot instructions

Meridian is a .NET semantic application-flow graph generator with CLI and MCP support.

For broad codebase analysis, feature placement, impact analysis, or framework-mediated flow tracing, prefer Meridian MCP first when available and fresh. Start with schema/summary-style graph orientation, then verify exact source files before editing.

For precise single-file edits or syntax-only changes, use direct source inspection first.

Use these docs as the canonical workflow references:

- `docs/agent-playbook.md`
- `docs/agent-quickstart.md`
- `docs/mcp.md`

When validating unreleased CLI behavior in this repo, run from source:

```powershell
dotnet run --project "src\Meridian.Cli\Meridian.Cli.csproj" -c Release -- <command>
```

Standard checks:

```powershell
dotnet format "Meridian.sln" --verify-no-changes
dotnet build "Meridian.sln" -c Release
dotnet test "Meridian.sln" -c Release
```

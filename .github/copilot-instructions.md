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

Standard PR/release checks:

```powershell
dotnet restore "Meridian.CI.slnf"
dotnet build "Meridian.CI.slnf" -c Release -warnaserror
dotnet test "Meridian.CI.slnf" -c Release --no-build
dotnet format "Meridian.CI.slnf" --verify-no-changes --no-restore
```

BenchmarkDotNet checks are intentionally separate from normal PR CI:

```powershell
dotnet build "tests\Meridian.Benchmarks\Meridian.Benchmarks.csproj" -c Release
dotnet run --project "tests\Meridian.Benchmarks\Meridian.Benchmarks.csproj" -c Release -- benchmarks --quick
```

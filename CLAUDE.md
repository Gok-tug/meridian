# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

Meridian is a .NET semantic application-flow graph generator with CLI and MCP support. It uses Roslyn/MSBuildWorkspace to load .NET solutions, emits a deterministic evidence-bearing graph, and models framework-mediated flow that plain call graphs miss: ASP.NET Core endpoints, dependency injection, MediatR/Mediator, EF Core, reflection/static dynamic wiring, source-generator hints, and conditional-flow facts.

Do not treat `0.x` alpha releases as production-ready or schema-stable. The current package version lives in `src\Meridian.Cli\Meridian.Cli.csproj`; completed release history belongs in `CHANGELOG.md`, and milestone planning belongs in `ROADMAP.md`.

## Common commands

Run normal PR/release checks with the CI solution filter:

```powershell
dotnet restore "Meridian.CI.slnf"
dotnet build "Meridian.CI.slnf" -c Release -warnaserror
dotnet test "Meridian.CI.slnf" -c Release --no-build
dotnet format "Meridian.CI.slnf" --verify-no-changes --no-restore
```

Run a single test project:

```powershell
dotnet test "tests\Meridian.AnalyzerTests\Meridian.AnalyzerTests.csproj" -c Release
```

Run a single test by filter:

```powershell
dotnet test "tests\Meridian.AnalyzerTests\Meridian.AnalyzerTests.csproj" -c Release --filter "FullyQualifiedName~BasicCalls"
```

Validate unreleased CLI behavior by running the CLI from source:

```powershell
dotnet run --project "src\Meridian.Cli\Meridian.Cli.csproj" -c Release -- <command>
```

Examples:

```powershell
dotnet run --project "src\Meridian.Cli\Meridian.Cli.csproj" -c Release -- scan "Meridian.CI.slnf" --output meridian-out --trust-project
dotnet run --project "src\Meridian.Cli\Meridian.Cli.csproj" -c Release -- agent-summary --graph meridian-out\graph.json --budget compact
dotnet run --project "src\Meridian.Cli\Meridian.Cli.csproj" -c Release -- mcp --graph meridian-out\graph.json
```

BenchmarkDotNet checks are intentionally separate from normal PR CI:

```powershell
dotnet build "tests\Meridian.Benchmarks\Meridian.Benchmarks.csproj" -c Release
dotnet run --project "tests\Meridian.Benchmarks\Meridian.Benchmarks.csproj" -c Release -- benchmarks --quick
```

## Architecture map

The high-level pipeline is:

```text
load solution
  -> create Roslyn workspace context
  -> index projects, compilations, symbols, syntax trees
  -> run foundation analyzers
  -> run framework fact analyzers
  -> link cross-framework flow
  -> build graph
  -> normalize and validate graph
  -> export/query/report
```

Key projects:

- `src\Meridian.Abstractions`: shared graph, confidence, evidence, analyzer/exporter, and diagnostic contracts.
- `src\Meridian.Core`: graph construction, validation, identity, deterministic ordering, duplicate handling, and query primitives.
- `src\Meridian.Roslyn`: MSBuildWorkspace loading, compilation/symbol access, source mapping, foundation facts, and framework-aware analyzer passes.
- `src\Meridian.Exporters.Json`: graph JSON export.
- `src\Meridian.Mcp`: MCP tools over precomputed `graph.json` files; it does not run Roslyn live during tool calls.
- `src\Meridian.Cli`: command-line entrypoint for `scan`, `explain`, `path`, `agent-summary`, and `mcp`.

Test projects mirror the runtime areas: `Meridian.Core.Tests`, `Meridian.AnalyzerTests`, `Meridian.Mcp.Tests`, `Meridian.Cli.Tests`, and the separate `Meridian.Benchmarks` project. `samples\Sample.*` projects are analyzer fixtures for direct calls, DI, MediatR, EF Core, dynamic wiring, member graph, ASP.NET Core flow, MVVM flow, and conditional flow.

Analyzer execution order is a correctness boundary. Keep foundation facts, framework facts, cross-framework linking, normalization, and diagnostics as explicit phases rather than allowing analyzers to race or infer the same flow independently.

## Meridian MCP workflow

For broad graph-worthy .NET questions, prefer Meridian MCP first when the client exposes MCP tools and the graph is fresh:

- codebase orientation,
- feature placement and impact analysis,
- endpoint-to-handler, DI, MediatR/Mediator, EF Core, reflection, or dynamic-wiring flow tracing,
- path/dependency questions between routes, symbols, services, handlers, and persistence types.

Use direct source inspection first for exact single-file fixes, syntax-only changes, stale/missing graph situations, unsupported analyzer domains, and final verification before edits.

Canonical workflow docs:

- `docs\agent-playbook.md`
- `docs\agent-quickstart.md`
- `docs\mcp.md`

Meridian MCP answers from a precomputed graph. If source changes, MCP results about changed code are stale until `meridian scan` is rerun and the running MCP server reloads the graph or restarts. Do not treat missing graph facts as proof that source behavior is absent.

## Versioning and changelog policy

- Do not bump `src\Meridian.Cli\Meridian.Cli.csproj` version unless the user explicitly asks for release preparation or a version bump.
- For ordinary unreleased work, add changelog entries under `## Unreleased`; do not open a new release section proactively.
- Only move `Unreleased` entries into `## <version> — <title>` during explicit release preparation, and the section version must match the package version being released.
- `CHANGELOG.md` is completed release history; `ROADMAP.md` is milestone planning. Do not duplicate roadmap plans into changelog release sections.

## Release notes workflow

The manual `Release` workflow publishes the NuGet package and can optionally create a GitHub release. GitHub release notes are extracted from only the matching `CHANGELOG.md` version section; the workflow should not use the whole changelog as release notes.

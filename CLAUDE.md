# Meridian agent instructions

Meridian is a .NET semantic application-flow graph generator with CLI and MCP support.

For broad analysis in this repo, prefer Meridian MCP first when it is configured and the graph is fresh. Use `docs/agent-playbook.md` as the canonical workflow. Use direct source inspection for exact file edits, unsupported graph domains, and final verification before changing code.

When validating unreleased Meridian CLI changes, run the CLI from source:

```powershell
dotnet run --project "src\Meridian.Cli\Meridian.Cli.csproj" -c Release -- <command>
```

Common checks:

```powershell
dotnet format "Meridian.sln" --verify-no-changes
dotnet build "Meridian.sln" -c Release
dotnet test "Meridian.sln" -c Release
```

After source edits, Meridian MCP results about changed code are stale until `meridian scan` is rerun and the running MCP server reloads the graph or restarts. Do not treat missing graph facts as proof that source behavior is absent.

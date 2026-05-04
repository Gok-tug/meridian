# Releasing

Meridian should use explicit prerelease versions until the analyzer pipeline and graph schema are stable.

## Versioning

Use SemVer prerelease versions:

```text
0.1.0-alpha.1
0.1.0-alpha.2
0.2.0-alpha.1
0.3.0-alpha.1
```

Avoid publishing stable-looking versions such as `0.1.0` while the project is still alpha.

## Package metadata

NuGet metadata should include:

- package ID: `meridian`
- package type: .NET global tool
- command name: `meridian`
- license expression: MIT
- repository URL
- project URL when available
- package tags:
  - dotnet
  - roslyn
  - code-analysis
  - flow-graph
  - mcp
  - mediatr
  - aspnetcore

## Manual release flow

Recommended early release flow:

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet pack --configuration Release
```

Then publish manually after reviewing package contents:

```bash
dotnet nuget push artifacts/package/meridian.0.1.0-alpha.1.nupkg --source nuget.org
```

The actual API key should never be committed.

## CI release flow

A future GitHub Actions release workflow should:

1. restore,
2. build,
3. test,
4. run golden-file analyzer tests,
5. pack,
6. upload artifacts,
7. publish only from tagged releases or manual approval.

Publishing should not happen automatically from every push to main.

## Release checklist

Before publishing:

- version updated in project files,
- changelog updated,
- README status accurate,
- roadmap updated,
- CLI docs match implementation,
- graph schema version documented,
- golden tests pass,
- package installs locally as a global tool,
- `meridian --help` works,
- generated package does not include build artifacts or secrets.

## Local package validation

Before pushing to NuGet:

```bash
dotnet tool install --global --add-source ./artifacts/package meridian --version 0.1.0-alpha.1
meridian --help
dotnet tool uninstall --global meridian
```

## Changelog policy

Each release should include:

- added analyzers,
- CLI changes,
- graph schema changes,
- confidence model changes,
- performance changes,
- known limitations.

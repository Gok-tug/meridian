# Releasing

Meridian should use explicit prerelease versions until the analyzer pipeline and graph schema are stable.

## Versioning

Use SemVer prerelease versions such as `0.6.0-alpha.1`.

The intended milestone sequence is tracked in [ROADMAP.md](../ROADMAP.md). Completed release history is tracked in [CHANGELOG.md](../CHANGELOG.md). Avoid publishing stable-looking versions such as `0.1.0` while the project is still alpha.

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

## Manual GitHub Actions release flow

The preferred release path is the manually triggered `Release` workflow in `.github/workflows/release.yml`. Publishing should not happen automatically from every push to main.

Before running the workflow:

1. Update and commit the intended version in `src/Meridian.Cli/Meridian.Cli.csproj`.
2. Move release-ready entries from `Unreleased` into a matching `CHANGELOG.md` section such as `## 0.6.0-alpha.1 — ...`.
3. Update README/docs and roadmap status as needed.
4. Configure the GitHub repository secret `NUGET_API_KEY`.
5. Optionally configure the GitHub environment `nuget-release` with required reviewers for manual approval.
6. Run the `Release` workflow manually with the exact version input, for example `0.6.0-alpha.1`.

The workflow validates that the input version matches the project file, restores/builds/tests the PR-safe `Meridian.CI.slnf` filter with warnings as errors, checks formatting, checks vulnerable packages, packs the CLI tool, validates the package artifact, installs it locally as a tool, runs `meridian --help` and `meridian mcp --help`, checks that generated artifacts did not dirty the working tree, prepares GitHub release notes from the single matching `CHANGELOG.md` version section when requested, and then publishes the package to NuGet. BenchmarkDotNet and dogfood evidence are produced by the separate manual/scheduled `Benchmarks` workflow, not by release publish.

The workflow intentionally does not use `--skip-duplicate`; rerunning a published version should fail visibly. If optional GitHub release creation is enabled, the workflow fails before publishing when the matching changelog section is missing or empty.

By default, create the Git tag manually after NuGet publish succeeds:

```bash
git tag v<version>
git push origin v<version>
```

The workflow has an optional `create_github_release` input. Leave it false unless you want the workflow to create `v<version>` and attach the package artifact as a GitHub release after NuGet publish. When this is enabled, release notes come from only the matching `CHANGELOG.md` version section, not from the full changelog.

## Local fallback release flow

Use the local flow only as a fallback when GitHub Actions is unavailable.

```bash
dotnet restore Meridian.CI.slnf
dotnet build Meridian.CI.slnf --configuration Release -warnaserror
dotnet test Meridian.CI.slnf --configuration Release
dotnet format Meridian.CI.slnf --verify-no-changes
dotnet pack src/Meridian.Cli/Meridian.Cli.csproj --configuration Release --output artifacts/package
```

Validate the generated package locally:

```bash
dotnet tool install --tool-path artifacts/tool-smoke --add-source artifacts/package meridian --version <version>
artifacts/tool-smoke/meridian --help
artifacts/tool-smoke/meridian mcp --help
```

Then publish manually after reviewing package contents:

```bash
dotnet nuget push artifacts/package/meridian.<version>.nupkg --source "https://api.nuget.org/v3/index.json" --api-key <NUGET_API_KEY>
```

The actual API key should never be committed.

## Release checklist

Before publishing:

- version updated in project files,
- matching changelog section exists for the release version,
- README status accurate,
- roadmap updated,
- CLI docs match implementation,
- graph schema version documented,
- golden tests pass,
- CLI smoke tests validate exit codes and generated graph contents when CLI smoke coverage exists,
- MCP freshness workflow, agent quickstart, skill preview, and agent playbook are reviewed,
- package installs locally as a global tool,
- `meridian --help` works,
- generated package does not include build artifacts or secrets.

## Local package validation

Before pushing to NuGet:

```bash
dotnet tool install --global --add-source ./artifacts/package meridian --version <version>
meridian --help
dotnet tool uninstall --global meridian
```

## Changelog policy

`CHANGELOG.md` is the release-notes source of truth. Keep `Unreleased` for work that is not release-ready yet, and move entries into a `## <version> — <title>` section before publishing that version.

Each release section should include relevant:

- added analyzers,
- CLI changes,
- graph schema changes,
- confidence model changes,
- performance changes,
- known limitations.

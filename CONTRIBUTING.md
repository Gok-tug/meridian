# Contributing to Meridian

Meridian is in early alpha. Contributions should prioritize correctness, evidence, deterministic output, and clear documentation over broad feature coverage.

## Development principles

- Prefer semantic Roslyn analysis over text matching.
- Add evidence for graph facts whenever possible.
- Use `EXTRACTED`, `INFERRED`, and `AMBIGUOUS` consistently.
- Keep analyzer packs modular.
- Do not add broad framework support without tests and documented limitations.
- Keep graph JSON deterministic so golden tests and diffs remain useful.

## Expected workflow

```bash
dotnet restore
dotnet build
dotnet test
```

Before opening a pull request, run formatting and tests:

```bash
dotnet format --verify-no-changes
dotnet test
```

## Analyzer changes

Analyzer changes should include golden-file tests.

Recommended layout:

```text
tests/
  Meridian.AnalyzerTests/
    MediatR/
      Fixtures/
        GetOrderQuery/
      Expected/
        GetOrderQuery.graph.json
      GetOrderQueryTests.cs
```

A good analyzer test should:

1. load a small fixture project or solution,
2. run the relevant analyzer set,
3. normalize graph output,
4. compare against an expected `.graph.json` file,
5. assert confidence and evidence values, not only node counts.

## Documentation changes

Documentation should be updated when a command, graph relation, analyzer behavior, confidence rule, or limitation changes.

The README should stay honest about implemented behavior. Planned behavior belongs in `ROADMAP.md` or the relevant `docs/` page.

## Issue reports

Useful issue reports include:

- Meridian version
- .NET SDK version
- operating system
- command used
- expected output
- actual output
- minimal project or graph fixture when possible

## Pull request checklist

- Tests added or updated
- Golden files updated intentionally
- Documentation updated
- Public graph schema changes documented
- Performance impact considered
- Limitations documented if behavior is incomplete or ambiguous

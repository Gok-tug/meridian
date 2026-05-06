# Dogfood Audit

Date: 2026-05-06

This audit checks whether Meridian is useful against real open-source .NET repositories, not only against curated samples.

## Scope

Repositories were cloned shallowly under `.dogfood/` and scanned with the current Release build of `src/Meridian.Cli`.

| Repository | Commit | Target | Notes |
| --- | --- | --- | --- |
| `ardalis/CleanArchitecture` | `d79c69852db4ddf80efe0be53355720e38820211` | `.dogfood/CleanArchitecture/Clean.Architecture.slnx` | ASP.NET Core, FastEndpoints, `Mediator.SourceGenerator`, EF Core, `.slnx` solution format. |
| `dotnet-architecture/eShopOnWeb` | `4da8212117e87d808d4bbc7da6286fd2147ce606` | `.dogfood/eShopOnWeb/eShopOnWeb.sln` | ASP.NET Core MVC/Razor/API, MediatR, EF Core, DI. |
| `alper-han/CrossMacro` | `fbe4fd52cd8b76449fc1f771fcc39834114540d5` | `.dogfood/CrossMacro/CrossMacro.sln` | Avalonia desktop app, MVVM, DI, platform abstractions, native OS boundaries, CLI command/router patterns. |

Environment:

- .NET SDK: `10.0.103`
- Meridian generator: initial audit `0.4.0-alpha.3`; follow-up validation `0.4.0-alpha.4`
- CLI build: `dotnet build src/Meridian.Cli/Meridian.Cli.csproj -c Release`

The scan warning about MSBuild project evaluation appeared as expected. Public repositories were not scanned with `--trust-project`, so the trust-boundary diagnostic remains visible in generated graphs.

## Result summary

Meridian is already usable as an alpha repository-orientation tool on real .NET codebases. It successfully produced graphs for both repositories, handled `.slnx`, identified classic MediatR and EF Core flows in eShopOnWeb, generated useful `agent-summary` output, and returned actionable ambiguity/no-path messages.

The initial `0.4.0-alpha.3` product gap was framework coverage rather than runtime stability: ASP.NET endpoint semantics, FastEndpoints/MinimalApi.Endpoint, and the `Mediator.SourceGenerator` package were not represented as first-class flow edges yet.

A follow-up validation after `0.4.0-alpha.4` implementation confirmed the top gaps are now represented for the static patterns covered by this milestone:

| Case | Nodes | Edges | Endpoint nodes | Diagnostics | Confirmed path |
| --- | ---: | ---: | ---: | ---: | --- |
| CleanArchitecture `.slnx` | 233 | 322 | 5 | 1 | `POST /Contributors -> CreateContributorHandler` succeeds via `calls -> sends -> handled_by`. |
| eShopOnWeb solution | 947 | 1,655 | 34 | 6 | `GET /Order/MyOrders -> GetMyOrdersHandler` succeeds via `calls -> sends -> handled_by`. |

The eShopOnWeb diagnostics are now more accurately classified: NuGet advisory messages are warnings, while the unsupported Docker Compose `.dcproj` project remains an error.

## Restore and scan metrics

| Case | Restore | Scan | Exit | Nodes | Edges | Diagnostics | Graph size |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CleanArchitecture `.slnx` | 6.83s | 9.00s | 0 | 228 | 304 | 1 | 403,966 bytes |
| CleanArchitecture Web project | 7.17s | 7.70s | 0 | 104 | 120 | 1 | 156,398 bytes |
| eShopOnWeb solution | 11.95s | 14.75s | 0 | 913 | 1,621 | 6 | 1,819,232 bytes |

CleanArchitecture `.slnx` initially produced one AspireHost workspace diagnostic before a full solution restore. After `dotnet restore .dogfood/CleanArchitecture/Clean.Architecture.slnx`, the rescan produced only the expected trust-boundary warning.

eShopOnWeb restore completed, but NuGet emitted vulnerability warnings for the repository's own dependencies:

- `System.Text.Json` `8.0.3`: high severity advisories.
- `Azure.Identity` `1.10.4`: moderate severity advisories.

Meridian surfaced these MSBuild/NuGet audit messages as `MERIDIAN_WORKSPACE` diagnostics with `failure` severity even though the scan exited successfully. That is useful visibility, but the severity mapping is too coarse for non-fatal MSBuild warnings.

## Graph coverage by repository

### CleanArchitecture

Primary `.slnx` scan:

| Node kind | Count |
| --- | ---: |
| `type` | 78 |
| `method` | 75 |
| `property` | 48 |
| `field` | 26 |
| `dbcontext` | 1 |

| Relation | Count |
| --- | ---: |
| `contains` | 142 |
| `reads` | 84 |
| `calls` | 37 |
| `reflects` | 9 |
| `uses` | 9 |
| `injects` | 7 |
| `implemented_by` | 5 |
| `registered_as` | 5 |
| `writes` | 4 |
| `queries` | 2 |

Positive signal:

- `.slnx` scanning works.
- EF Core use can be traced, e.g. `SeedData.InitializeAsync -> AppDbContext` succeeds with a `uses` edge.
- `explain Create.ExecuteAsync` correctly resolves method, field reads, request property reads, and normal method calls.

Initial coverage gaps before `0.4.0-alpha.4`:

- FastEndpoints route configuration was not emitted as endpoint nodes or route edges. Examples:
  - `.dogfood/CleanArchitecture/src/Clean.Architecture.Web/Contributors/Create.cs:23` configures `Post(CreateContributorRequest.Route)`.
  - `.dogfood/CleanArchitecture/src/Clean.Architecture.Web/Contributors/List.cs:12` configures `Get("/Contributors")`.
- `Mediator.SourceGenerator` was not detected as MediatR-like mediator flow. Example:
  - `.dogfood/CleanArchitecture/src/Clean.Architecture.Web/Contributors/Create.cs:54` calls `_mediator.Send(new CreateContributorCommand(...))`.
  - `path Create.ExecuteAsync CreateContributorCommand` returned `No path found` with exit code 5.

Follow-up status after `0.4.0-alpha.4`:

- FastEndpoints route facts are emitted for the static `Configure()` verb patterns covered by this milestone.
- `Mediator.SourceGenerator` request/handler and `Send` patterns are represented with the same mediator graph relations used for MediatR.
- `POST /Contributors -> CreateContributorHandler` now succeeds through `calls -> sends -> handled_by`.

### eShopOnWeb

| Node kind | Count |
| --- | ---: |
| `property` | 288 |
| `method` | 260 |
| `type` | 226 |
| `field` | 128 |
| `enum_member` | 4 |
| `mediatr_request` | 2 |
| `dbcontext` | 2 |
| `mediatr_handler` | 2 |
| `enum` | 1 |

| Relation | Count |
| --- | ---: |
| `contains` | 662 |
| `reads` | 504 |
| `calls` | 160 |
| `writes` | 145 |
| `injects` | 48 |
| `uses` | 27 |
| `implemented_by` | 25 |
| `registered_as` | 21 |
| `reflects` | 21 |
| `queries` | 4 |
| `handled_by` | 2 |
| `sends` | 2 |

Positive signal:

- Classic MediatR is useful today.
  - `.dogfood/eShopOnWeb/src/Web/Controllers/OrderController.cs:26` sends `new GetMyOrders(...)`.
  - `path OrderController.MyOrders GetMyOrdersHandler` succeeded with `sends -> handled_by`.
- EF Core static access is useful today.
  - `.dogfood/eShopOnWeb/src/Infrastructure/Data/Queries/BasketQueryService.cs:24` accesses `_dbContext.Baskets`.
  - `path BasketQueryService.CountTotalBasketItems CatalogContext.BasketItems` succeeded.
- Ambiguity UX is useful. Querying `IReadRepository` returned candidate node IDs instead of guessing.

Initial coverage gaps before `0.4.0-alpha.4`:

- ASP.NET Core controller route/action metadata was not represented as endpoint flow.
  - `.dogfood/eShopOnWeb/src/Web/Controllers/OrderController.cs:12` has `[Route("[controller]/[action]")]`.
  - `.dogfood/eShopOnWeb/src/Web/Controllers/OrderController.cs:22` and `.dogfood/eShopOnWeb/src/Web/Controllers/OrderController.cs:31` define `[HttpGet]` actions.
- MinimalApi.Endpoint route registration was not represented as an endpoint-to-handler/service path.
  - `.dogfood/eShopOnWeb/src/PublicApi/CatalogItemEndpoints/CatalogItemListPagedEndpoint.cs:29` defines `AddRoute`.
  - `.dogfood/eShopOnWeb/src/PublicApi/CatalogItemEndpoints/CatalogItemListPagedEndpoint.cs:31` maps `api/catalog-items`.
  - `path CatalogItemListPagedEndpoint CatalogItemViewModelService` returned `No path found`.

Follow-up status after `0.4.0-alpha.4`:

- MVC controller route/action metadata is emitted as endpoint flow for the static attribute patterns covered by this milestone.
- MinimalApi.Endpoint `AddRoute` patterns are represented for statically resolved route delegates.
- `GET /Order/MyOrders -> GetMyOrdersHandler` now succeeds through `calls -> sends -> handled_by`.

### CrossMacro

CrossMacro was added as a larger, different-shape dogfood target after the ASP.NET-focused repositories. It is useful because it stresses desktop/UI, MVVM, DI, platform abstraction, native boundaries, and CLI-style runtime wiring rather than HTTP endpoint flow.

| Scan | Nodes | Edges | Diagnostics | Notes |
| --- | ---: | ---: | ---: | --- |
| Default trusted solution scan after alpha4 hardening | 6,721 | 22,149 | 0 | Best representation for agent workflow and production-source orientation; includes 4 additional direct DI alias registration edges. |
| `--include-tests` scan before hardening | 10,001 | 43,099 | 0 | Useful for stress and test-specific analysis, but summaries can shift toward fixtures. |

Evidence sanity on the pre-hardening default scan was strong:

- `22,145` edges checked.
- `0` missing evidence files.
- `0` out-of-range evidence lines.
- `475` distinct evidence files.
- Compact `agent-summary` output stayed practical for agent context use at roughly `6.2k` characters / `129` lines.

Positive signal:

- Emitted Roslyn semantic facts appear high-precision: direct C# calls, type/member containment, constructor injection, direct generic DI, interface implementations, fields/properties, and source-visible platform abstractions matched sampled source evidence.
- Graph navigation worked for source-visible flows such as `MainWindowViewModel -> EditorViewModel` constructor injection, `IPlatformServiceRegistrar -> LinuxPlatformServiceRegistrar` implementation, and platform call chains such as `WindowsInputSimulator -> User32.SendInput`.
- The scan produced no diagnostics and no generated-source noise such as designer or generated command nodes.

Observed recall gaps:

- Avalonia/XAML bindings are not emitted as graph facts yet. Examples include `x:DataType`, `DataContext`, `Binding`, and `Command` markup.
- CommunityToolkit.Mvvm generated members are not emitted yet. Source methods annotated with `[RelayCommand]` are present, but generated `*Command` properties are absent.
- CLI command/router wiring through resolver dictionaries, delegates, and factories is runtime wiring and remains outside current static graph coverage.
- Native boundaries such as `DllImport` / `LibraryImport` declarations are not first-class graph facts yet.
- Advanced DI factories are only partly covered. Direct generic registrations and direct `new Implementation(...)` factories work; delegate factories, `.Create()` factory calls, and `Func<T>` patterns remain planned. A narrow direct `GetRequiredService<TImplementation>()` alias pattern was prioritized for alpha4 hardening because it is source-resolved and common in CrossMacro.

Accuracy call:

- Precision is high for emitted graph facts because they are Roslyn/evidence backed.
- Recall is good for ordinary source-visible C# and direct framework patterns.
- Unsupported dynamic/runtime/UI/native wiring should be checked with targeted source inspection before claiming absence or full coverage.
- For agent workflow, default scans are preferable unless the task is explicitly about tests; `--include-tests` is valuable for stress validation but may skew centrality toward test fixtures.

## CLI command behavior

| Command | Case | Result |
| --- | --- | --- |
| `agent-summary --budget compact` | CleanArchitecture | Exit 0, 0.18s, 105 lines. |
| `agent-summary --budget compact` | eShopOnWeb | Exit 0, 0.21s, 120 lines. |
| `explain Create.ExecuteAsync` | CleanArchitecture | Exit 0, useful incoming/outgoing relations. |
| `explain GetMyOrders` | eShopOnWeb | Exit 0, correctly shows `mediatr_request`, incoming `sends`, outgoing `handled_by`. |
| `path "POST /Contributors" CreateContributorHandler` | CleanArchitecture | Exit 0, found `calls -> sends -> handled_by` after FastEndpoints and `Mediator.SourceGenerator` support. |
| `path OrderController.MyOrders GetMyOrdersHandler` | eShopOnWeb | Exit 0, found `sends -> handled_by`. |
| `path BasketQueryService.CountTotalBasketItems CatalogContext.BasketItems` | eShopOnWeb | Exit 0, found EF Core context/DbSet path. |
| `path GetMyOrdersHandler.Handle IReadRepository` | eShopOnWeb | Exit 5, ambiguity reported with candidate node IDs. |

No unhandled CLI exceptions were observed during this audit.

## Meridian test and coverage baseline

The initial coverage run used `dotnet test Meridian.sln -c Release --collect:"XPlat Code Coverage"` and passed:

| Test assembly | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| `Meridian.Core.Tests` | 13 | 0 | 0 |
| `Meridian.Mcp.Tests` | 49 | 0 | 0 |
| `Meridian.AnalyzerTests` | 8 | 0 | 0 |
| `Meridian.Cli.Tests` | 11 | 0 | 0 |
| Total | 81 | 0 | 0 |

Later `0.4.0-alpha.4` hardening verification, without collecting coverage, passed `86` tests across the same solution. The coverage percentages below remain the initial coverage baseline until the coverage run is repeated.

Raw Cobertura outputs were generated per test assembly. A best-effort merged line coverage estimate across unique source lines is:

- Merged line coverage: `3030/3633` lines, about `83.4%`.

Raw per-collector coverage outputs:

| Covered packages | Line coverage | Branch coverage |
| --- | ---: | ---: |
| `Meridian.Abstractions`, `Meridian.Core`, `Meridian.Exporters.Json` | 606/742, 81.7% | 201/316, 63.6% |
| `Meridian.Abstractions`, `Meridian.Core`, `Meridian.Exporters.Json`, `Meridian.Roslyn` | 1356/2207, 61.4% | 649/1238, 52.4% |
| `Meridian.Abstractions`, `Meridian.Core`, `Meridian.Exporters.Json`, `Meridian.Mcp` | 1440/1796, 80.2% | 504/706, 71.4% |
| `Meridian.Abstractions`, `Meridian.Cli`, `Meridian.Core`, `Meridian.Exporters.Json`, `Meridian.Mcp`, `Meridian.Roslyn` | 1159/3633, 31.9% | 388/1824, 21.3% |

Lowest merged line coverage files with at least 20 tracked lines:

| File | Coverage |
| --- | ---: |
| `Meridian.Cli/Commands/ExplainCommand.cs` | 0/24, 0.0% |
| `Meridian.Cli/Commands/PathCommand.cs` | 0/35, 0.0% |
| `Meridian.Mcp/MeridianMcpServer.cs` | 0/28, 0.0% |
| `Meridian.Cli/Rendering/GraphConsoleRenderer.cs` | 55/121, 45.5% |
| `Meridian.Roslyn/RoslynProjectLoader.cs` | 13/24, 54.2% |

The CLI coverage gap is partly a measurement problem: process-level CLI tests verify public behavior, but child process execution is not attributed to the test host's coverage collector. Add direct command-level tests or a coverage strategy for the spawned CLI process before treating CLI command files as truly untested.

`dotnet list Meridian.sln package --vulnerable` found no vulnerable packages in Meridian projects.

## Priority recommendation status

| Recommendation from the initial audit | Status | Notes |
| --- | --- | --- |
| Add ASP.NET Core endpoint analysis | Done in `0.4.0-alpha.4` for the static patterns covered by this milestone | MVC attributes, Minimal API verb calls, simple local `MapGroup`, FastEndpoints verbs, and MinimalApi.Endpoint route registration are represented. Runtime routing semantics remain out of scope. |
| Add `Mediator.SourceGenerator` support alongside MediatR | Done in `0.4.0-alpha.4` for source-resolved request, command, query, notification, handler, `Send`, and `Publish` patterns | Meridian reuses the existing mediator graph node kinds and relations so agents can reason across MediatR and `Mediator` packages consistently. |
| Improve workspace diagnostic severity mapping | Done in `0.4.0-alpha.4` | NuGet advisory messages are warnings while unsupported project-load failures remain errors. |
| Add dogfood fixtures or scripted audits to CI/manual validation | Partially done | Small in-repo fixtures now cover ASP.NET, FastEndpoints, MinimalApi.Endpoint, and mediator patterns. Full external repository scans should remain manual or scheduled, not per PR. A repeatable dogfood checklist/script is still useful for `0.5`. |
| Improve CLI coverage measurement | Still useful | Process-level tests verify public CLI behavior, but child process execution is not attributed to command classes by the current coverage collection approach. Direct command-level tests or a child-process coverage strategy would make coverage numbers more representative. |

## Current readiness call

Meridian can be used today on trusted real repositories as an alpha graph and agent-context tool. After the `0.4.0-alpha.4` follow-up, it can claim preview ASP.NET endpoint flow coverage for the static MVC, Minimal API, FastEndpoints, MinimalApi.Endpoint, and mediator patterns validated here. It is still not production-stable and should not claim runtime route behavior, authorization/filter/middleware behavior, model binding, or dynamic endpoint discovery.

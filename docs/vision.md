# Vision

Meridian exists to make modern .NET application flow understandable.

The first public version should feel like a serious engineering tool, not an experiment that happens to parse C# syntax. That means Meridian needs a clear scope, a stable graph model, documented limitations, tests that prove analyzer behavior, and an architecture that can grow beyond one framework.

## Problem

A real .NET request path often crosses boundaries that direct call graph tools miss:

```text
HTTP endpoint
  -> MediatR request
  -> handler resolved by DI
  -> injected service interface
  -> concrete implementation registered elsewhere
  -> EF Core DbContext
  -> database entity/table
```

Some links are explicit C# calls. Others are framework conventions, dependency injection registrations, generic type relationships, attributes, or reflection patterns.

Meridian should connect those links and explain why they exist.

## Product position

Meridian is a semantic application-flow graph generator for .NET applications.

It should help:

- developers understand unfamiliar codebases,
- maintainers audit high-risk flows,
- reviewers answer impact-analysis questions,
- AI agents navigate code with graph context instead of blind grep,
- teams document architecture from source facts.

## What makes Meridian valuable

A generic C# syntax graph is not enough. Meridian becomes useful when it understands .NET application architecture:

- ASP.NET Core endpoints
- MVC controllers and actions
- Minimal APIs
- dependency injection registrations
- constructor injection
- MediatR requests, handlers, sends, and publishes
- EF Core DbContext and DbSet usage
- reflection and assembly scanning
- future native/Rust interop boundaries

## Design philosophy

### Start narrow, but deep

The MVP should analyze fewer frameworks well instead of producing shallow output for many technologies.

The first valuable target is:

```text
ASP.NET Core endpoint -> MediatR request -> handler -> injected service -> implementation
```

### Evidence over magic

Meridian should show where each fact came from. If an edge is inferred from convention or assembly scanning, the graph should say so.

### AI-agent ready from early versions

MCP support should arrive early because querying a graph is one of Meridian's strongest use cases. The graph should be compact enough for agents and precise enough to reduce hallucinated code navigation.

### Extensible by analyzer packs

MediatR should not define the whole product. It is one high-value analyzer pack. The same architecture should support EF Core, messaging frameworks, background jobs, validation, mapping, and native interop later.

## Rust position

Rust is not part of the initial .NET MVP as a full Rust static analyzer.

Rust support should begin as native interop boundary detection for .NET applications:

- `DllImport`
- `LibraryImport`
- native DLL references
- generated bindings
- FFI boundary nodes
- calls into Rust-backed native libraries

Full Rust call graph analysis can remain future research.

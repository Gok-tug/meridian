# Confidence Model

Meridian must be honest about what it knows.

Every important edge in the graph should include a confidence category and, when possible, a numeric confidence score.

## Categories

```text
EXTRACTED
INFERRED
AMBIGUOUS
```

## EXTRACTED

Use `EXTRACTED` when Meridian has direct semantic or framework evidence.

Examples:

- Roslyn resolves a method call target.
- A class implements `IRequestHandler<GetOrderQuery, OrderDto>`.
- A method calls `sender.Send(new GetOrderQuery(...))` and Roslyn resolves the request type.
- An in-scope local `var query = new GetOrderQuery(...); await sender.Send(query);` resolves to one request type before reassignment.
- `services.AddScoped<IOrderRepository, EfOrderRepository>()` directly maps an abstraction to an implementation.
- `services.AddSingleton<IClock>(_ => new SystemClock())` directly creates a source-resolved implementation in a factory lambda.
- A controller action has `[HttpGet("/orders/{id}")]`.
- A property is declared as `DbSet<Order>`.
- Code references `typeof(OrderService)`.

Recommended score:

```text
0.95 - 1.00
```

## INFERRED

Use `INFERRED` when Meridian has strong pattern-based evidence but no direct symbol edge.

Examples:

- Scrutor assembly scanning likely registers implementations by convention.
- MediatR assembly registration implies handlers may be discovered at runtime.
- A concrete MediatR message parameter type strongly implies the request or notification target.
- Configuration binding implies an options type.
- Reflection uses constrained type filters such as `IsAssignableFrom`.

Recommended score:

```text
0.50 - 0.94
```

## AMBIGUOUS

Use `AMBIGUOUS` when multiple targets are possible or runtime behavior is required.

Examples:

- `Activator.CreateInstance(type)` where `type` comes from runtime data.
- `Assembly.Load(name)` with a non-constant name.
- service registration through broad assembly scanning with multiple matching types.
- dynamic dispatch where a single concrete target cannot be selected.
- conditional compilation or runtime environment controls the selected implementation.

Recommended score:

```text
0.00 - 0.49
```

## Evidence

Confidence without evidence is not enough. Important edges should explain why they exist.

Example:

```json
{
  "relation": "handled_by",
  "confidence": "EXTRACTED",
  "confidence_score": 1.0,
  "evidence": {
    "file": "Features/Orders/GetOrderQueryHandler.cs",
    "line": 8,
    "reason": "IRequestHandler<GetOrderQuery, OrderDto>"
  }
}
```

## Diagnostics for uncertainty

When ambiguity affects graph quality, Meridian should emit diagnostics.

Example:

```json
{
  "id": "MERIDIAN_DI_AMBIGUOUS_REGISTRATION",
  "severity": "warning",
  "message": "Multiple possible implementations found for IOrderRepository.",
  "source_file": "Program.cs",
  "source_location": "L22"
}
```

## Policy by analyzer

| Analyzer | EXTRACTED | INFERRED | AMBIGUOUS |
| --- | --- | --- | --- |
| Roslyn direct calls | resolved invocation symbol | constrained static type inference | dynamic target |
| ASP.NET Core | route attributes and Map methods | route conventions | runtime route construction |
| Dependency Injection | direct generic registration; direct object creation factory registration | convention/scanning registration | broad runtime scanning or complex factories |
| MediatR | generic request/handler match; inline or in-scope local object creation dispatch | concrete parameter type dispatch fallback | runtime-created request |
| EF Core | DbSet and DbContext symbols | repository/entity naming patterns | dynamic Set(Type) |
| Reflection | typeof/nameof constant target | constrained scan | runtime string/type |
| Native interop | DllImport/LibraryImport constant | generated binding hints | runtime library name |

## Golden tests

Analyzer tests should assert confidence. A test that only checks that an edge exists is incomplete if the edge confidence is wrong.

Golden files should include:

- relation,
- confidence,
- confidence score when stable,
- evidence reason,
- source location when deterministic.

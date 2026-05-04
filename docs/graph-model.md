# Graph Model

Meridian's graph model is the public contract between analyzers, exporters, the CLI, and MCP tools.

The graph must be deterministic, versioned, and evidence-bearing.

## Top-level document

```json
{
  "schema_version": "0.1",
  "generator": "Meridian",
  "generator_version": "0.2.0-alpha.3",
  "root": "C:/src/MyApp",
  "nodes": [],
  "edges": [],
  "diagnostics": []
}
```

`schema_version` is required. Any breaking graph contract change must update the schema version.

## Node shape

```json
{
  "id": "type:MyApp.Features.Orders.GetOrderQuery",
  "label": "GetOrderQuery",
  "kind": "mediatr_request",
  "symbol": "MyApp.Features.Orders.GetOrderQuery",
  "source_file": "Features/Orders/GetOrderQuery.cs",
  "source_location": "L12",
  "metadata": {}
}
```

Required fields:

- `id`
- `label`
- `kind`

Recommended fields:

- `symbol`
- `source_file`
- `source_location`
- `metadata`

## Edge shape

```json
{
  "source": "method:MyApp.Features.Orders.OrderController.GetById(System.Guid id)",
  "target": "type:MyApp.Features.Orders.GetOrderQuery",
  "relation": "sends",
  "confidence": "EXTRACTED",
  "confidence_score": 1.0,
  "evidence": {
    "file": "Controllers/OrdersController.cs",
    "line": 42,
    "symbol": "MyApp.Features.Orders.OrderController.GetById(System.Guid id)",
    "reason": "Roslyn resolved MediatR Send call to 'MyApp.Features.Orders.GetOrderQuery' from inline object creation."
  },
  "metadata": {}
}
```

Required fields:

- `source`
- `target`
- `relation`
- `confidence`

Recommended fields:

- `confidence_score`
- `evidence`
- `metadata`

## Node kinds

Initial node kinds:

```text
project
namespace
type
method
endpoint
controller
action
minimal_api
service_registration
service_interface
service_implementation
mediatr_request
mediatr_notification
mediatr_handler
dbcontext
dbset
entity
reflection_site
assembly_scan
native_library
ffi_boundary
```

Analyzer packs may add new node kinds, but additions must be documented.

## Edge relations

Initial relations:

```text
contains
calls
returns
uses
creates
sends
publishes
handled_by
registered_as
injects
implemented_by
queries
maps_to
configured_by
reflects
scans
loads
crosses_boundary
```

Relation meanings:

| Relation | Meaning |
| --- | --- |
| `contains` | Project/file/type ownership or hierarchy |
| `calls` | Direct method invocation resolved by Roslyn |
| `uses` | Symbol, type, service, or DbContext usage |
| `creates` | Object creation expression |
| `sends` | Method or endpoint dispatches a MediatR request |
| `publishes` | Method or endpoint publishes a MediatR notification |
| `handled_by` | Message/request handled by handler type |
| `registered_as` | DI registration maps abstraction to implementation |
| `injects` | Constructor or parameter injection dependency |
| `implemented_by` | Interface/base abstraction implemented by concrete type |
| `queries` | EF Core query access to DbSet/entity |
| `configured_by` | Registration or options configured by code site |
| `reflects` | Reflection references a type/member |
| `scans` | Assembly scanning may register or discover types |
| `loads` | Runtime assembly/native library loading |
| `crosses_boundary` | Flow crosses into native/Rust/external boundary |

## Confidence

Every edge must declare one of:

```text
EXTRACTED
INFERRED
AMBIGUOUS
```

See [confidence-model.md](confidence-model.md).

## Diagnostics

Diagnostics document problems or uncertainty during analysis.

```json
{
  "id": "MERIDIAN001",
  "severity": "warning",
  "message": "Multiple possible implementations found for IOrderRepository.",
  "source_file": "Program.cs",
  "source_location": "L18"
}
```

Recommended severities:

```text
info
warning
error
```

## Determinism rules

Graph output should be stable across runs when source code has not changed.

Rules:

- sort nodes by `id`,
- sort edges by `source`, `target`, `relation`, then evidence location,
- avoid timestamps in golden-test output,
- normalize path separators in tests,
- use stable symbol IDs where possible,
- merge duplicate edges deterministically.

## Schema evolution

Breaking changes require a schema version bump.

Examples of breaking changes:

- removing a required field,
- changing relation meaning,
- changing node ID format,
- changing confidence enum values.

Non-breaking changes:

- adding optional metadata,
- adding a new node kind,
- adding a new relation with docs,
- adding diagnostics.

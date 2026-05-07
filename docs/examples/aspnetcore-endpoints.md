# Example: ASP.NET Core Endpoints

ASP.NET Core endpoint analysis is available as a preview. Meridian emits synthetic `endpoint` nodes when route patterns are statically visible, then connects them to source handlers or directly to mediator messages when supported endpoint lambdas dispatch messages inline.

## MVC controller

```csharp
[ApiController]
[Route("[controller]")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet("{id}")]
    public Task<OrderDto> GetById(Guid id)
    {
        // action logic
    }
}
```

Expected endpoint label:

```text
GET /orders/{id}
```

Expected relation:

```text
GET /orders/{id} --calls--> OrdersController.GetById
```

Meridian currently replaces common `[controller]` and `[action]` route tokens.

## Minimal API method group

```csharp
app.MapPost("/orders", CreateOrder);

static async Task<IResult> CreateOrder(CreateOrderCommand command, ISender sender)
{
    var result = await sender.Send(command);
    return Results.Ok(result);
}
```

Expected relations:

```text
POST /orders --calls--> CreateOrder
CreateOrder --sends--> CreateOrderCommand
CreateOrderCommand --handled_by--> CreateOrderCommandHandler
```

## Minimal API `MapGroup`

```csharp
var api = app.MapGroup("/api");
api.MapGet("/orders/{id}", GetById);
```

Expected endpoint label:

```text
GET /api/orders/{id}
```

`MapGroup` support is intentionally narrow: the group prefix must be a simple local value in the same block before the mapping call.

## Inline endpoint mediator dispatch

```csharp
app.MapPost("/inline-orders", async (IMediator mediator) =>
{
    return Results.Ok(await mediator.Send(new CreateOrderCommand("inline")));
});
```

Expected relation:

```text
POST /inline-orders --sends--> CreateOrderCommand
```

Inline lambdas do not get invented delegate nodes. Meridian emits direct endpoint-level `sends` or `publishes` edges only when the mediator message is statically resolved.

## FastEndpoints

```csharp
public sealed class CreateOrderEndpoint : Endpoint<CreateOrderRequest, OrderResult>
{
    public override void Configure()
    {
        Post("/fast/orders");
    }

    public override Task<OrderResult> ExecuteAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        // handler logic
    }
}
```

Expected relation:

```text
POST /fast/orders --calls--> CreateOrderEndpoint.ExecuteAsync
```

Meridian currently links `Get(...)`, `Post(...)`, `Put(...)`, `Delete(...)`, and `Patch(...)` calls in `Configure()` to same-type `ExecuteAsync` or `HandleAsync` when present.

## MinimalApi.Endpoint-style route registration

```csharp
public sealed class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderRepository>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/minimalapi/orders", async (CreateOrderRequest request, IOrderRepository repository) =>
        {
            return await HandleAsync(request, repository);
        });
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderRepository repository)
    {
        // handler logic
    }
}
```

Expected relation:

```text
POST /api/minimalapi/orders --calls--> CreateOrderEndpoint.HandleAsync
```

## Path query

```bash
meridian path "POST /orders" "CreateOrderCommandHandler"
```

Abridged expected output:

```text
POST /orders
  --calls--> CreateOrder
  --sends--> CreateOrderCommand
  --handled_by--> CreateOrderCommandHandler
```

## Current limits

Meridian does not model ASP.NET Core route precedence, authorization, filters, middleware, model binding, runtime route discovery, generated endpoint code execution, or arbitrary endpoint delegate dataflow. Dynamic routes and unresolved source handlers produce diagnostics instead of guessed edges.

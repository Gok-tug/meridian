# Example: ASP.NET Core Endpoints

ASP.NET Core endpoint analysis is planned for a later alpha milestone. The analyzer should discover both MVC controller actions and Minimal API routes.

## MVC controller

```csharp
[ApiController]
[Route("orders")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet("{id}")]
    public Task<OrderDto> GetById(Guid id)
    {
        // action logic
    }
}
```

Expected endpoint node:

```text
endpoint:GET:/orders/{id}
```

Expected relation:

```text
GET /orders/{id} --calls--> OrdersController.GetById
```

## Minimal API

```csharp
app.MapPost("/orders", async (CreateOrderCommand command, ISender sender) =>
{
    return await sender.Send(command);
});
```

Expected endpoint node:

```text
endpoint:POST:/orders
```

Expected relations:

```text
POST /orders --calls--> minimal_api_delegate
POST /orders --sends--> CreateOrderCommand
```

## Path query

```bash
meridian path "POST /orders" "CreateOrderCommandHandler"
```

Expected output:

```text
POST /orders
  --sends--> CreateOrderCommand
  --handled_by--> CreateOrderCommandHandler
```

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Sample.AspNetCoreFlow;

public interface IOrderRepository
{
    Task<OrderResult> AddAsync(CreateOrderCommand command);
}

public sealed class CreateOrderMinimalApiEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderRepository>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/minimalapi/orders", async (CreateOrderRequest request, IOrderRepository repository) =>
        {
            return await HandleAsync(request, repository);
        });
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderRepository repository)
    {
        var result = await repository.AddAsync(new CreateOrderCommand(request.OrderId));
        return Results.Ok(result);
    }
}

using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Sample.AspNetCoreFlow;

public static class MinimalApis
{
    public const string OrdersRoute = "/orders";

    public static void MapRoutes(IEndpointRouteBuilder app, IMediator mediator)
    {
        app.MapPost(OrdersRoute, CreateOrder);

        var api = app.MapGroup("/api");
        api.MapGet("/orders/{orderId}", GetOrder);

        app.MapPost("/inline-orders", async (IMediator sender) =>
        {
            var result = await sender.Send(new CreateOrderCommand("inline"));
            return Results.Ok(result);
        });
    }

    public static async Task<IResult> CreateOrder(CreateOrderCommand command, IMediator mediator)
    {
        var result = await mediator.Send(command);
        return Results.Ok(result);
    }

    public static async Task<IResult> GetOrder(int orderId, IMediator mediator)
    {
        var result = await mediator.Send(new GetOrderQuery(orderId));
        await mediator.Publish(new OrderViewedNotification(orderId));
        return Results.Ok(result);
    }
}

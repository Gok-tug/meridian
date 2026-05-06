using FastEndpoints;
using Mediator;

namespace Sample.AspNetCoreFlow;

public sealed class CreateOrderFastEndpoint : Endpoint<CreateOrderRequest, OrderResult>
{
    private readonly IMediator _mediator;

    public CreateOrderFastEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post(CreateOrderRequest.Route);
    }

    public override async Task<OrderResult> ExecuteAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        return await _mediator.Send(new CreateOrderCommand(request.OrderId), cancellationToken);
    }
}

public sealed class ListOrdersFastEndpoint : Endpoint<ListOrdersRequest, OrderResult>
{
    private readonly IMediator _mediator;

    public ListOrdersFastEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/fast/orders/{orderId}");
    }

    public override async Task HandleAsync(ListOrdersRequest request, CancellationToken cancellationToken = default)
    {
        await _mediator.Publish(new OrderViewedNotification(request.OrderId), cancellationToken);
    }
}

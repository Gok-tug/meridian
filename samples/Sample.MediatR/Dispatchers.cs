using MediatR;
using Sample.MediatR.Contracts;

namespace Sample.MediatR;

public sealed class OrderDispatchSamples
{
    public async Task<OrderDto> DispatchInlineRequest(IMediator mediator, CancellationToken cancellationToken)
    {
        return await mediator.Send(new GetOrderQuery(100), cancellationToken);
    }

    public async Task<OrderDto> DispatchLocalRequest(ISender sender, CancellationToken cancellationToken)
    {
        var query = new GetOrderQuery(101);
        return await sender.Send(query, cancellationToken);
    }

    public async Task<OrderDto> DispatchNestedLocalRequest(ISender sender, CancellationToken cancellationToken)
    {
        var query = new GetOrderQuery(105);
        try
        {
            return await sender.Send(query, cancellationToken);
        }
        catch
        {
            throw;
        }
    }

    public Func<Task<OrderDto>> CreateLambdaDispatch(ISender sender, CancellationToken cancellationToken)
    {
        var query = new GetOrderQuery(106);
        return async () => await sender.Send(query, cancellationToken);
    }

    public async Task DispatchLocalCommand(IMediator mediator, CancellationToken cancellationToken)
    {
        var command = new RefreshCacheCommand();
        await mediator.Send(command, cancellationToken);
    }

    public async Task<OrderDto> DispatchExternalRequest(IMediator mediator, CancellationToken cancellationToken)
    {
        var query = new ExternalOrderQuery(102);
        return await mediator.Send(query, cancellationToken);
    }

    public async Task PublishInlineNotification(IMediator mediator, CancellationToken cancellationToken)
    {
        await mediator.Publish(new OrderUpdatedNotification(103), cancellationToken);
    }

    public async Task PublishWithPublisher(IPublisher publisher, CancellationToken cancellationToken)
    {
        await publisher.Publish(new OrderUpdatedNotification(104), cancellationToken);
    }

    public async Task<OrderDto> DispatchParameterRequest(ISender sender, GetOrderQuery query, CancellationToken cancellationToken)
    {
        return await sender.Send(query, cancellationToken);
    }

    public async Task PublishParameterNotification(IPublisher publisher, OrderUpdatedNotification notification, CancellationToken cancellationToken)
    {
        await publisher.Publish(notification, cancellationToken);
    }
}

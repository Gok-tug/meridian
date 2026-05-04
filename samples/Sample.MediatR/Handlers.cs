using MediatR;

namespace Sample.MediatR;

public interface IGetOrderQueryHandlerContract : IRequestHandler<GetOrderQuery, OrderDto>
{
}

public abstract class AbstractGetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    public abstract Task<OrderDto> Handle(GetOrderQuery request, CancellationToken cancellationToken);
}

public sealed class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    public Task<OrderDto> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new OrderDto(request.OrderId, "open"));
    }
}

public sealed class RefreshCacheCommandHandler : IRequestHandler<RefreshCacheCommand>
{
    public Task Handle(RefreshCacheCommand request, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class OrderUpdatedNotificationHandler : INotificationHandler<OrderUpdatedNotification>
{
    public Task Handle(OrderUpdatedNotification notification, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

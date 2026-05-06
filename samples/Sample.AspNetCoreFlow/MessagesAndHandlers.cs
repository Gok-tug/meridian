using Mediator;

namespace Sample.AspNetCoreFlow;

public sealed record CreateOrderCommand(string OrderId) : ICommand<OrderResult>;

public sealed record GetOrderQuery(int OrderId) : IQuery<OrderResult>;

public sealed record OrderViewedNotification(int OrderId) : INotification;

public sealed record CreateOrderRequest(string OrderId)
{
    public const string Route = "/fast/orders";
}

public sealed record ListOrdersRequest(int OrderId);

public sealed record OrderResult(string OrderId);

public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, OrderResult>
{
    public ValueTask<OrderResult> Handle(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new OrderResult(command.OrderId));
    }
}

public sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderResult>
{
    public ValueTask<OrderResult> Handle(GetOrderQuery query, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new OrderResult(query.OrderId.ToString()));
    }
}

public sealed class OrderViewedNotificationHandler : INotificationHandler<OrderViewedNotification>
{
    public ValueTask Handle(OrderViewedNotification notification, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}

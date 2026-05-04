using MediatR;

namespace Sample.MediatR;

public sealed record GetOrderQuery(int OrderId) : IRequest<OrderDto>;

public sealed record RefreshCacheCommand : IRequest;

public sealed record WatchOrderStream(int OrderId) : IStreamRequest<OrderDto>;

public sealed record OrderUpdatedNotification(int OrderId) : INotification;

public sealed record OrderDto(int OrderId, string Status);

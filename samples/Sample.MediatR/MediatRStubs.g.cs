using System.Threading;
using System.Threading.Tasks;

namespace MediatR;

public interface IRequest
{
}

public interface IRequest<out TResponse>
{
}

public interface INotification
{
}

public interface IStreamRequest<out TResponse>
{
}

public interface IRequestHandler<in TRequest>
    where TRequest : IRequest
{
    Task Handle(TRequest request, CancellationToken cancellationToken);
}

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}

public interface IStreamRequestHandler<in TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

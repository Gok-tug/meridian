namespace Mediator
{
    public interface IRequest;

    public interface IRequest<TResponse>;

    public interface ICommand;

    public interface ICommand<TResponse>;

    public interface IQuery<TResponse>;

    public interface INotification;

    public interface ISender
    {
        ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

        ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

        ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

        ValueTask Send(ICommand command, CancellationToken cancellationToken = default);
    }

    public interface IPublisher
    {
        ValueTask Publish(INotification notification, CancellationToken cancellationToken = default);
    }

    public interface IMediator : ISender, IPublisher;

    public interface IRequestHandler<TRequest, TResponse>;

    public interface ICommandHandler<TCommand>;

    public interface ICommandHandler<TCommand, TResponse>;

    public interface IQueryHandler<TQuery, TResponse>;

    public interface INotificationHandler<TNotification>;
}

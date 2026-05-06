using Meridian.Abstractions;
using Microsoft.CodeAnalysis;

namespace Meridian.Roslyn;

internal sealed class MediatRSymbolClassifier
{
    private const string MediatRNamespace = "MediatR";
    private const string MediatorNamespace = "Mediator";
    private const string RequestInterface = "IRequest";
    private const string GenericRequestInterface = "IRequest`1";
    private const string CommandInterface = "ICommand";
    private const string GenericCommandInterface = "ICommand`1";
    private const string GenericQueryInterface = "IQuery`1";
    private const string NotificationInterface = "INotification";
    private const string StreamRequestInterface = "IStreamRequest`1";
    private const string RequestHandlerInterface = "IRequestHandler`1";
    private const string GenericRequestHandlerInterface = "IRequestHandler`2";
    private const string CommandHandlerInterface = "ICommandHandler`1";
    private const string GenericCommandHandlerInterface = "ICommandHandler`2";
    private const string GenericQueryHandlerInterface = "IQueryHandler`2";
    private const string NotificationHandlerInterface = "INotificationHandler`1";
    private const string StreamRequestHandlerInterface = "IStreamRequestHandler`2";

    public string ClassifyType(INamedTypeSymbol typeSymbol)
    {
        if (IsConcreteHandlerType(typeSymbol) && GetHandledMessages(typeSymbol).Any())
        {
            return GraphNodeKinds.MediatRHandler;
        }

        if (IsRequestLikeType(typeSymbol))
        {
            return GraphNodeKinds.MediatRRequest;
        }

        if (IsNotificationType(typeSymbol))
        {
            return GraphNodeKinds.MediatRNotification;
        }

        return GraphNodeKinds.Type;
    }

    public bool IsRequestType(INamedTypeSymbol typeSymbol)
    {
        return ImplementsMediatRInterface(typeSymbol, RequestInterface) ||
            ImplementsMediatRInterface(typeSymbol, GenericRequestInterface) ||
            ImplementsMediatRInterface(typeSymbol, CommandInterface) ||
            ImplementsMediatRInterface(typeSymbol, GenericCommandInterface) ||
            ImplementsMediatRInterface(typeSymbol, GenericQueryInterface);
    }

    public bool IsStreamRequestType(INamedTypeSymbol typeSymbol)
    {
        return ImplementsMediatRInterface(typeSymbol, StreamRequestInterface);
    }

    public bool IsRequestLikeType(INamedTypeSymbol typeSymbol)
    {
        return IsRequestType(typeSymbol) || IsStreamRequestType(typeSymbol);
    }

    public bool IsNotificationType(INamedTypeSymbol typeSymbol)
    {
        return ImplementsMediatRInterface(typeSymbol, NotificationInterface);
    }

    public IEnumerable<MediatRHandledMessage> GetHandledMessages(INamedTypeSymbol handlerSymbol)
    {
        if (!IsConcreteHandlerType(handlerSymbol))
        {
            yield break;
        }

        foreach (var interfaceSymbol in handlerSymbol.AllInterfaces
            .OrderBy(symbol => symbol.ToDisplayString(SymbolDisplay.TypeFormat), StringComparer.Ordinal))
        {
            var originalDefinition = interfaceSymbol.OriginalDefinition;
            if (!IsMediatRInterface(originalDefinition))
            {
                continue;
            }

            switch (originalDefinition.MetadataName)
            {
                case RequestHandlerInterface when interfaceSymbol.TypeArguments.Length == 1 &&
                    interfaceSymbol.TypeArguments[0] is INamedTypeSymbol requestSymbol:
                    yield return new MediatRHandledMessage(requestSymbol, interfaceSymbol);
                    break;

                case GenericRequestHandlerInterface when interfaceSymbol.TypeArguments.Length == 2 &&
                    interfaceSymbol.TypeArguments[0] is INamedTypeSymbol requestSymbol:
                    yield return new MediatRHandledMessage(requestSymbol, interfaceSymbol);
                    break;

                case CommandHandlerInterface when interfaceSymbol.TypeArguments.Length == 1 &&
                    interfaceSymbol.TypeArguments[0] is INamedTypeSymbol commandSymbol:
                    yield return new MediatRHandledMessage(commandSymbol, interfaceSymbol);
                    break;

                case GenericCommandHandlerInterface when interfaceSymbol.TypeArguments.Length == 2 &&
                    interfaceSymbol.TypeArguments[0] is INamedTypeSymbol commandSymbol:
                    yield return new MediatRHandledMessage(commandSymbol, interfaceSymbol);
                    break;

                case GenericQueryHandlerInterface when interfaceSymbol.TypeArguments.Length == 2 &&
                    interfaceSymbol.TypeArguments[0] is INamedTypeSymbol querySymbol:
                    yield return new MediatRHandledMessage(querySymbol, interfaceSymbol);
                    break;

                case NotificationHandlerInterface when interfaceSymbol.TypeArguments.Length == 1 &&
                    interfaceSymbol.TypeArguments[0] is INamedTypeSymbol notificationSymbol:
                    yield return new MediatRHandledMessage(notificationSymbol, interfaceSymbol);
                    break;

                case StreamRequestHandlerInterface when interfaceSymbol.TypeArguments.Length == 2 &&
                    interfaceSymbol.TypeArguments[0] is INamedTypeSymbol streamRequestSymbol:
                    yield return new MediatRHandledMessage(streamRequestSymbol, interfaceSymbol);
                    break;
            }
        }
    }

    private static bool IsConcreteHandlerType(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.TypeKind == TypeKind.Class && !typeSymbol.IsAbstract;
    }

    private static bool ImplementsMediatRInterface(INamedTypeSymbol typeSymbol, string metadataName)
    {
        return typeSymbol.AllInterfaces.Any(interfaceSymbol =>
            IsMediatRInterface(interfaceSymbol.OriginalDefinition) &&
            interfaceSymbol.OriginalDefinition.MetadataName.Equals(metadataName, StringComparison.Ordinal));
    }

    internal static bool IsSupportedMediatorNamespace(INamespaceSymbol namespaceSymbol)
    {
        var namespaceName = namespaceSymbol.ToDisplayString();
        return namespaceName.Equals(MediatRNamespace, StringComparison.Ordinal) ||
            namespaceName.Equals(MediatorNamespace, StringComparison.Ordinal);
    }

    private static bool IsMediatRInterface(INamedTypeSymbol interfaceSymbol)
    {
        return IsSupportedMediatorNamespace(interfaceSymbol.ContainingNamespace);
    }
}

internal readonly record struct MediatRHandledMessage(
    INamedTypeSymbol MessageSymbol,
    INamedTypeSymbol HandlerInterfaceSymbol);

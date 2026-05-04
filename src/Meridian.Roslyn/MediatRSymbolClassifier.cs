using Meridian.Abstractions;
using Microsoft.CodeAnalysis;

namespace Meridian.Roslyn;

internal sealed class MediatRSymbolClassifier
{
    private const string MediatRNamespace = "MediatR";
    private const string RequestInterface = "IRequest";
    private const string GenericRequestInterface = "IRequest`1";
    private const string NotificationInterface = "INotification";
    private const string StreamRequestInterface = "IStreamRequest`1";
    private const string RequestHandlerInterface = "IRequestHandler`1";
    private const string GenericRequestHandlerInterface = "IRequestHandler`2";
    private const string NotificationHandlerInterface = "INotificationHandler`1";
    private const string StreamRequestHandlerInterface = "IStreamRequestHandler`2";

    public string ClassifyType(INamedTypeSymbol typeSymbol)
    {
        if (IsConcreteHandlerType(typeSymbol) && GetHandledMessages(typeSymbol).Any())
        {
            return GraphNodeKinds.MediatRHandler;
        }

        if (ImplementsMediatRInterface(typeSymbol, RequestInterface) ||
            ImplementsMediatRInterface(typeSymbol, GenericRequestInterface) ||
            ImplementsMediatRInterface(typeSymbol, StreamRequestInterface))
        {
            return GraphNodeKinds.MediatRRequest;
        }

        if (ImplementsMediatRInterface(typeSymbol, NotificationInterface))
        {
            return GraphNodeKinds.MediatRNotification;
        }

        return GraphNodeKinds.Type;
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

    private static bool IsMediatRInterface(INamedTypeSymbol interfaceSymbol)
    {
        return interfaceSymbol.ContainingNamespace.ToDisplayString().Equals(MediatRNamespace, StringComparison.Ordinal);
    }
}

internal readonly record struct MediatRHandledMessage(
    INamedTypeSymbol MessageSymbol,
    INamedTypeSymbol HandlerInterfaceSymbol);

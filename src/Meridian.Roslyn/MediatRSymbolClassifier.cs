using Meridian.Abstractions;
using Microsoft.CodeAnalysis;

namespace Meridian.Roslyn;

internal sealed class MediatRSymbolClassifier
{
    private const string MediatRNamespace = "MediatR";

    public string ClassifyType(INamedTypeSymbol typeSymbol)
    {
        if (IsConcreteHandlerType(typeSymbol) && GetHandledMessages(typeSymbol).Any())
        {
            return GraphNodeKinds.MediatRHandler;
        }

        if (ImplementsMediatRInterface(typeSymbol, "IRequest") ||
            ImplementsMediatRInterface(typeSymbol, "IRequest`1"))
        {
            return GraphNodeKinds.MediatRRequest;
        }

        if (ImplementsMediatRInterface(typeSymbol, "INotification"))
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
                case "IRequestHandler`1" when interfaceSymbol.TypeArguments.Length == 1 &&
                    interfaceSymbol.TypeArguments[0] is INamedTypeSymbol requestSymbol:
                    yield return new MediatRHandledMessage(requestSymbol, interfaceSymbol);
                    break;

                case "IRequestHandler`2" when interfaceSymbol.TypeArguments.Length == 2 &&
                    interfaceSymbol.TypeArguments[0] is INamedTypeSymbol requestSymbol:
                    yield return new MediatRHandledMessage(requestSymbol, interfaceSymbol);
                    break;

                case "INotificationHandler`1" when interfaceSymbol.TypeArguments.Length == 1 &&
                    interfaceSymbol.TypeArguments[0] is INamedTypeSymbol notificationSymbol:
                    yield return new MediatRHandledMessage(notificationSymbol, interfaceSymbol);
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

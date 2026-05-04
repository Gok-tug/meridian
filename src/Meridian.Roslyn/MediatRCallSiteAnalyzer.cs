using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal sealed class MediatRCallSiteAnalyzer
{
    private const string MediatRNamespace = "MediatR";
    private readonly RoslynSourceFilter _sourceFilter;
    private readonly RoslynGraphFactory _graphFactory;
    private readonly MediatRSymbolClassifier _classifier;

    public MediatRCallSiteAnalyzer(
        RoslynSourceFilter sourceFilter,
        RoslynGraphFactory graphFactory,
        MediatRSymbolClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(sourceFilter);
        ArgumentNullException.ThrowIfNull(graphFactory);
        ArgumentNullException.ThrowIfNull(classifier);
        _sourceFilter = sourceFilter;
        _graphFactory = graphFactory;
        _classifier = classifier;
    }

    public void Analyze(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var methodName = InvocationSymbolResolver.GetInvokedMethodName(invocation);
        if (methodName is not ("Send" or "Publish"))
        {
            return;
        }

        var targetMethod = InvocationSymbolResolver.ResolveTargetMethod(semanticModel, invocation, cancellationToken);
        if (targetMethod is null || !IsMediatRDispatchMethod(targetMethod, methodName))
        {
            return;
        }

        var sourceSymbol = ResolveSourceMethod(invocation, semanticModel, cancellationToken);
        if (sourceSymbol is null || !_sourceFilter.HasAnalyzableSourceLocation(sourceSymbol))
        {
            return;
        }

        var messageArgument = SelectMessageArgument(invocation, methodName);
        if (messageArgument is null)
        {
            return;
        }

        var resolvedMessage = ResolveMessageType(messageArgument.Expression, invocation, semanticModel, cancellationToken);
        if (resolvedMessage is not { } message || !IsValidDispatchMessage(methodName, message.Type))
        {
            return;
        }

        var sourceNode = _graphFactory.CreateMethodNode(sourceSymbol);
        var targetNode = _graphFactory.CreateTypeNodeAllowingMissingSource(message.Type);
        graph.AddNode(sourceNode);
        graph.AddNode(targetNode);
        graph.AddEdge(new GraphEdge
        {
            Source = sourceNode.Id,
            Target = targetNode.Id,
            Relation = methodName == "Send" ? GraphRelations.Sends : GraphRelations.Publishes,
            Confidence = message.Confidence,
            ConfidenceScore = message.ConfidenceScore,
            Evidence = _graphFactory.CreateEvidence(
                invocation.GetLocation(),
                sourceNode.Symbol,
                $"Roslyn resolved MediatR {methodName} call to '{targetNode.Symbol}' from {message.Reason}.")
        });
    }

    private static IMethodSymbol? ResolveSourceMethod(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var sourceSymbol = semanticModel.GetEnclosingSymbol(invocation.SpanStart, cancellationToken) as IMethodSymbol;
        if (sourceSymbol?.MethodKind != MethodKind.AnonymousFunction)
        {
            return sourceSymbol;
        }

        return invocation.Ancestors()
            .OfType<BaseMethodDeclarationSyntax>()
            .Select(declaration => semanticModel.GetDeclaredSymbol(declaration, cancellationToken) as IMethodSymbol)
            .FirstOrDefault(symbol => symbol is not null) ?? sourceSymbol;
    }

    private static ArgumentSyntax? SelectMessageArgument(InvocationExpressionSyntax invocation, string methodName)
    {
        var preferredName = methodName == "Send" ? "request" : "notification";
        return invocation.ArgumentList.Arguments.FirstOrDefault(argument =>
                argument.NameColon?.Name.Identifier.ValueText.Equals(preferredName, StringComparison.Ordinal) == true) ??
            invocation.ArgumentList.Arguments.FirstOrDefault();
    }

    private MessageResolution? ResolveMessageType(
        ExpressionSyntax expression,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (TryResolveObjectCreationType(expression, semanticModel, cancellationToken) is { } inlineType)
        {
            return MessageResolution.Extracted(inlineType, "inline object creation");
        }

        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol is ILocalSymbol localSymbol)
        {
            return ResolveLocalMessageType(localSymbol, invocation, semanticModel, cancellationToken);
        }

        if (symbol is IParameterSymbol parameterSymbol && TryNormalizeMessageType(parameterSymbol.Type) is { } parameterType)
        {
            return MessageResolution.Inferred(parameterType, "static parameter type");
        }

        return null;
    }

    private MessageResolution? ResolveLocalMessageType(
        ILocalSymbol localSymbol,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var declaration = localSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .OrderBy(variable => variable.SpanStart)
            .FirstOrDefault();
        if (declaration is null || declaration.Initializer is null || declaration.SpanStart > invocation.SpanStart)
        {
            return null;
        }

        var declarationBlock = declaration.FirstAncestorOrSelf<BlockSyntax>();
        if (declarationBlock is null || !ContainsInvocation(declarationBlock, invocation))
        {
            return null;
        }

        if (HasAssignmentBetween(declaration.Identifier.ValueText, declarationBlock, declaration.Span.End, invocation.SpanStart))
        {
            return null;
        }

        return TryResolveObjectCreationType(declaration.Initializer.Value, semanticModel, cancellationToken) is { } initializerType
            ? MessageResolution.Extracted(initializerType, "in-scope local object creation")
            : null;
    }

    private static bool ContainsInvocation(BlockSyntax block, InvocationExpressionSyntax invocation)
    {
        return invocation.AncestorsAndSelf()
            .OfType<BlockSyntax>()
            .Any(candidate => ReferenceEquals(candidate, block));
    }

    private static bool HasAssignmentBetween(string localName, BlockSyntax block, int start, int end)
    {
        return block.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.SpanStart > start && assignment.SpanStart < end)
            .Any(assignment => assignment.Left is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText.Equals(localName, StringComparison.Ordinal));
    }

    private static INamedTypeSymbol? TryResolveObjectCreationType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is not ObjectCreationExpressionSyntax and not ImplicitObjectCreationExpressionSyntax)
        {
            return null;
        }

        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return TryNormalizeMessageType(typeInfo.Type ?? typeInfo.ConvertedType);
    }

    private static INamedTypeSymbol? TryNormalizeMessageType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType ||
            namedType.TypeKind is TypeKind.Error or TypeKind.Dynamic ||
            namedType.SpecialType == SpecialType.System_Object)
        {
            return null;
        }

        return namedType;
    }

    private bool IsValidDispatchMessage(string methodName, INamedTypeSymbol messageType)
    {
        return methodName switch
        {
            "Send" => _classifier.IsRequestType(messageType),
            "Publish" => _classifier.IsNotificationType(messageType),
            _ => false
        };
    }

    private static bool IsMediatRDispatchMethod(IMethodSymbol methodSymbol, string methodName)
    {
        if (!methodSymbol.Name.Equals(methodName, StringComparison.Ordinal))
        {
            return false;
        }

        return methodName switch
        {
            "Send" => IsMediatRDispatchType(methodSymbol.ContainingType, "IMediator", "ISender"),
            "Publish" => IsMediatRDispatchType(methodSymbol.ContainingType, "IMediator", "IPublisher"),
            _ => false
        };
    }

    private static bool IsMediatRDispatchType(INamedTypeSymbol? typeSymbol, params string[] acceptedNames)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        return IsMediatRNamedType(typeSymbol, acceptedNames) ||
            typeSymbol.AllInterfaces.Any(interfaceSymbol => IsMediatRNamedType(interfaceSymbol, acceptedNames));
    }

    private static bool IsMediatRNamedType(INamedTypeSymbol typeSymbol, params string[] acceptedNames)
    {
        return typeSymbol.ContainingNamespace.ToDisplayString().Equals(MediatRNamespace, StringComparison.Ordinal) &&
            acceptedNames.Contains(typeSymbol.Name, StringComparer.Ordinal);
    }

    private readonly record struct MessageResolution(
        INamedTypeSymbol Type,
        string Confidence,
        double ConfidenceScore,
        string Reason)
    {
        public static MessageResolution Extracted(INamedTypeSymbol type, string reason)
        {
            return new MessageResolution(type, ConfidenceLevels.Extracted, 1.0, reason);
        }

        public static MessageResolution Inferred(INamedTypeSymbol type, string reason)
        {
            return new MessageResolution(type, ConfidenceLevels.Inferred, 0.9, reason);
        }
    }
}

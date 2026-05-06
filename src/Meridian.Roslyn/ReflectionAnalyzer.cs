using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal sealed class ReflectionAnalyzer
{
    private const string DynamicTargetDiagnosticId = "MERIDIAN_REFLECTION_DYNAMIC_TARGET";

    private static readonly HashSet<string> MetadataOnlyTypeArgumentNames = new(StringComparer.Ordinal)
    {
        "clrType",
        "keyClrType",
        "oldClrType"
    };

    private readonly RoslynSourceFilter _sourceFilter;
    private readonly RoslynGraphFactory _graphFactory;

    public ReflectionAnalyzer(RoslynSourceFilter sourceFilter, RoslynGraphFactory graphFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFilter);
        ArgumentNullException.ThrowIfNull(graphFactory);
        _sourceFilter = sourceFilter;
        _graphFactory = graphFactory;
    }

    public void AnalyzeTypeOf(
        TypeOfExpressionSyntax typeOfExpression,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        if (IsArgumentToActivatorCreateInstance(typeOfExpression, semanticModel, cancellationToken) ||
            IsMetadataOnlyTypeArgument(typeOfExpression))
        {
            return;
        }

        var sourceMethod = GetSourceMethod(typeOfExpression, semanticModel, cancellationToken);
        var targetType = TryResolveType(typeOfExpression.Type, semanticModel, cancellationToken);
        if (sourceMethod is null || targetType is null)
        {
            return;
        }

        EmitReflectsEdge(sourceMethod, targetType, typeOfExpression.GetLocation(), typeOfExpression.ToString(), graph);
    }

    public void AnalyzeInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var methodSymbol = InvocationSymbolResolver.ResolveTargetMethod(semanticModel, invocation, cancellationToken);
        if (methodSymbol is null || !IsActivatorCreateInstance(methodSymbol))
        {
            return;
        }

        var sourceMethod = GetSourceMethod(invocation, semanticModel, cancellationToken);
        if (sourceMethod is null)
        {
            return;
        }

        var targetType = TryResolveGenericCreateInstanceTarget(methodSymbol, semanticModel, invocation, cancellationToken) ??
            TryResolveTypeOfArgument(invocation, semanticModel, cancellationToken);
        if (targetType is not null)
        {
            EmitReflectsEdge(sourceMethod, targetType, invocation.GetLocation(), invocation.ToString(), graph);
            return;
        }

        graph.AddDiagnostic(_graphFactory.CreateDiagnostic(
            invocation.GetLocation(),
            DynamicTargetDiagnosticId,
            "warning",
            $"Activator.CreateInstance target in '{invocation}' is not statically resolvable; no reflected type edge was emitted."));
    }

    private IMethodSymbol? GetSourceMethod(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var sourceMethod = semanticModel.GetEnclosingSymbol(node.SpanStart, cancellationToken) as IMethodSymbol;
        if (sourceMethod?.MethodKind == MethodKind.AnonymousFunction)
        {
            sourceMethod = node.Ancestors()
                .OfType<BaseMethodDeclarationSyntax>()
                .Select(declaration => semanticModel.GetDeclaredSymbol(declaration, cancellationToken) as IMethodSymbol)
                .FirstOrDefault(symbol => symbol is not null);
        }

        return sourceMethod is not null && _sourceFilter.HasAnalyzableSourceLocation(sourceMethod)
            ? sourceMethod
            : null;
    }

    private INamedTypeSymbol? TryResolveGenericCreateInstanceTarget(
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var typeArguments = InvocationSymbolResolver.ResolveGenericTypeArguments(methodSymbol, semanticModel, invocation, cancellationToken);
        return typeArguments.Count == 1 ? typeArguments[0] : null;
    }

    private static INamedTypeSymbol? TryResolveTypeOfArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return TryGetActivatorTypeArgument(invocation)?.Expression is TypeOfExpressionSyntax typeOfExpression
            ? TryResolveType(typeOfExpression.Type, semanticModel, cancellationToken)
            : null;
    }

    private static INamedTypeSymbol? TryResolveType(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(typeSyntax, cancellationToken);
        return TryNormalizeNamedType(typeInfo.Type ?? typeInfo.ConvertedType);
    }

    private static ArgumentSyntax? TryGetActivatorTypeArgument(InvocationExpressionSyntax invocation)
    {
        var namedTypeArgument = invocation.ArgumentList.Arguments.FirstOrDefault(static argument =>
            argument.NameColon?.Name.Identifier.ValueText.Equals("type", StringComparison.Ordinal) == true);
        if (namedTypeArgument is not null)
        {
            return namedTypeArgument;
        }

        var firstPositionalArgument = invocation.ArgumentList.Arguments.FirstOrDefault(static argument => argument.NameColon is null);
        return firstPositionalArgument?.Expression is TypeOfExpressionSyntax ? firstPositionalArgument : null;
    }

    private void EmitReflectsEdge(
        IMethodSymbol sourceMethod,
        INamedTypeSymbol targetType,
        Location location,
        string expression,
        GraphBuilder graph)
    {
        var sourceNode = _graphFactory.CreateMethodNode(sourceMethod);
        var targetNode = _graphFactory.CreateTypeNodeAllowingMissingSource(targetType);
        graph.AddNode(sourceNode);
        graph.AddNode(targetNode);
        graph.AddEdge(new GraphEdge
        {
            Source = sourceNode.Id,
            Target = targetNode.Id,
            Relation = GraphRelations.Reflects,
            Confidence = ConfidenceLevels.Extracted,
            ConfidenceScore = 1.0,
            Evidence = _graphFactory.CreateEvidence(
                location,
                sourceNode.Symbol,
                $"Roslyn resolved reflection target '{expression}' to '{targetNode.Symbol}'.")
        });
    }

    private static bool IsMetadataOnlyTypeArgument(TypeOfExpressionSyntax typeOfExpression)
    {
        return typeOfExpression.Parent is ArgumentSyntax argument &&
            argument.NameColon?.Name.Identifier.ValueText is { } argumentName &&
            MetadataOnlyTypeArgumentNames.Contains(argumentName);
    }

    private static bool IsArgumentToActivatorCreateInstance(
        TypeOfExpressionSyntax typeOfExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (typeOfExpression.Parent is not ArgumentSyntax argument ||
            argument.Parent is not ArgumentListSyntax argumentList ||
            argumentList.Parent is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        var methodSymbol = InvocationSymbolResolver.ResolveTargetMethod(semanticModel, invocation, cancellationToken);
        return methodSymbol is not null &&
            IsActivatorCreateInstance(methodSymbol) &&
            TryGetActivatorTypeArgument(invocation) == argument;
    }

    private static bool IsActivatorCreateInstance(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Name.Equals("CreateInstance", StringComparison.Ordinal) &&
            methodSymbol.ContainingType?.MetadataName.Equals("Activator", StringComparison.Ordinal) == true &&
            methodSymbol.ContainingType.ContainingNamespace.ToDisplayString().Equals("System", StringComparison.Ordinal);
    }

    private static INamedTypeSymbol? TryNormalizeNamedType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType || namedType.TypeKind is TypeKind.Error or TypeKind.Dynamic)
        {
            return null;
        }

        return namedType;
    }
}

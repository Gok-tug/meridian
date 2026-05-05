using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal sealed class ReflectionAnalyzer
{
    private const string DynamicTargetDiagnosticId = "MERIDIAN_REFLECTION_DYNAMIC_TARGET";

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
        if (IsArgumentToActivatorCreateInstance(typeOfExpression, semanticModel, cancellationToken))
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
        var typeArgument = invocation.ArgumentList.Arguments.FirstOrDefault(static argument =>
            argument.NameColon?.Name.Identifier.ValueText.Equals("type", StringComparison.Ordinal) == true &&
            argument.Expression is TypeOfExpressionSyntax);
        typeArgument ??= invocation.ArgumentList.Arguments.FirstOrDefault(static argument =>
            argument.NameColon is null && argument.Expression is TypeOfExpressionSyntax);

        return typeArgument?.Expression is TypeOfExpressionSyntax typeOfExpression
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
        return methodSymbol is not null && IsActivatorCreateInstance(methodSymbol);
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

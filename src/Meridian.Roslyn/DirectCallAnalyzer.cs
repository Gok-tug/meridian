using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal sealed class DirectCallAnalyzer
{
    private readonly RoslynSourceFilter _sourceFilter;
    private readonly RoslynGraphFactory _graphFactory;

    public DirectCallAnalyzer(RoslynSourceFilter sourceFilter, RoslynGraphFactory graphFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFilter);
        ArgumentNullException.ThrowIfNull(graphFactory);
        _sourceFilter = sourceFilter;
        _graphFactory = graphFactory;
    }

    public void Analyze(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var sourceSymbol = ResolveSourceMethod(invocation, semanticModel, cancellationToken);
        var targetSymbol = InvocationSymbolResolver.ResolveTargetMethod(semanticModel, invocation, cancellationToken);
        if (sourceSymbol is null || targetSymbol is null)
        {
            return;
        }

        if (!_sourceFilter.HasAnalyzableSourceLocation(sourceSymbol) || !_sourceFilter.HasAnalyzableSourceLocation(targetSymbol))
        {
            return;
        }

        var sourceNode = _graphFactory.CreateMethodNode(sourceSymbol);
        var targetNode = _graphFactory.CreateMethodNode(targetSymbol);
        graph.AddNode(sourceNode);
        graph.AddNode(targetNode);

        graph.AddEdge(new GraphEdge
        {
            Source = sourceNode.Id,
            Target = targetNode.Id,
            Relation = GraphRelations.Calls,
            Confidence = ConfidenceLevels.Extracted,
            ConfidenceScore = 1.0,
            Evidence = _graphFactory.CreateEvidence(
                invocation.GetLocation(),
                sourceNode.Symbol,
                $"Roslyn resolved invocation '{invocation.Expression}' to '{targetNode.Symbol}'.")
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
            .FirstOrDefault(symbol => symbol is not null);
    }
}

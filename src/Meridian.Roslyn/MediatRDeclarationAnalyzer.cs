using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal sealed class MediatRDeclarationAnalyzer
{
    private readonly RoslynSourceFilter _sourceFilter;
    private readonly RoslynGraphFactory _graphFactory;
    private readonly MediatRSymbolClassifier _classifier;
    private readonly HashSet<string> _handledByEdges = new(StringComparer.Ordinal);

    public MediatRDeclarationAnalyzer(
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
        TypeDeclarationAnalysisResult typeResult,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var handlerNode = typeResult.Node;
        foreach (var handledMessage in _classifier.GetHandledMessages(typeResult.Symbol))
        {
            if (!_sourceFilter.HasAnalyzableSourceLocation(handledMessage.MessageSymbol))
            {
                continue;
            }

            var messageNode = _graphFactory.CreateTypeNode(handledMessage.MessageSymbol);
            var edgeKey = string.Join('', messageNode.Id, handlerNode.Id, GraphRelations.HandledBy);
            if (!_handledByEdges.Add(edgeKey))
            {
                continue;
            }

            graph.AddNode(messageNode);
            graph.AddEdge(new GraphEdge
            {
                Source = messageNode.Id,
                Target = handlerNode.Id,
                Relation = GraphRelations.HandledBy,
                Confidence = ConfidenceLevels.Extracted,
                ConfidenceScore = 1.0,
                Evidence = _graphFactory.CreateEvidence(
                    FindHandlerInterfaceLocation(typeResult.Symbol, handledMessage.HandlerInterfaceSymbol, semanticModel, cancellationToken),
                    handlerNode.Symbol,
                    $"Roslyn resolved MediatR handler interface '{handledMessage.HandlerInterfaceSymbol.ToDisplayString(SymbolDisplay.TypeFormat)}'.")
            });
        }
    }

    private Location FindHandlerInterfaceLocation(
        INamedTypeSymbol handlerSymbol,
        INamedTypeSymbol handlerInterfaceSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var syntaxReference in handlerSymbol.DeclaringSyntaxReferences
            .OrderBy(reference => SourcePath.Normalize(reference.SyntaxTree.FilePath ?? string.Empty), StringComparer.Ordinal)
            .ThenBy(reference => reference.Span.Start))
        {
            if (syntaxReference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration ||
                declaration.BaseList is null)
            {
                continue;
            }

            foreach (var baseType in declaration.BaseList.Types.OrderBy(type => type.SpanStart))
            {
                var baseModel = semanticModel.Compilation.GetSemanticModel(baseType.SyntaxTree);
                var baseSymbol = baseModel.GetTypeInfo(baseType.Type, cancellationToken).Type as INamedTypeSymbol;
                if (SymbolEqualityComparer.Default.Equals(baseSymbol, handlerInterfaceSymbol))
                {
                    return baseType.GetLocation();
                }
            }
        }

        return _sourceFilter.FirstAnalyzableSourceLocation(handlerSymbol);
    }
}

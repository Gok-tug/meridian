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
            if (IsUnresolvedType(handledMessage.MessageSymbol))
            {
                var handlerInterfaceLocation = FindHandlerInterfaceLocation(typeResult.Symbol, handledMessage.HandlerInterfaceSymbol, semanticModel, cancellationToken);
                var evidence = _graphFactory.CreateEvidence(
                    handlerInterfaceLocation,
                    handlerNode.Symbol,
                    $"Roslyn could not resolve MediatR message type '{handledMessage.MessageSymbol.ToDisplayString(SymbolDisplay.TypeFormat)}'.");
                graph.AddDiagnostic(new GraphDiagnostic
                {
                    Id = "MERIDIAN_MEDIATR_UNRESOLVED_MESSAGE",
                    Severity = "warning",
                    Message = $"MediatR handler '{handlerNode.Symbol}' references an unresolved message type.",
                    SourceFile = evidence.File,
                    SourceLocation = evidence.Line is null ? null : $"L{evidence.Line}"
                });
                continue;
            }

            var messageNode = _graphFactory.CreateTypeNodeAllowingMissingSource(handledMessage.MessageSymbol);
            var edgeKey = string.Join('', messageNode.Id, handlerNode.Id, GraphRelations.HandledBy);
            if (!_handledByEdges.Add(edgeKey))
            {
                continue;
            }

            var edgeLocation = FindHandlerInterfaceLocation(typeResult.Symbol, handledMessage.HandlerInterfaceSymbol, semanticModel, cancellationToken);
            graph.AddNode(messageNode);
            graph.AddEdge(new GraphEdge
            {
                Source = messageNode.Id,
                Target = handlerNode.Id,
                Relation = GraphRelations.HandledBy,
                Confidence = ConfidenceLevels.Extracted,
                ConfidenceScore = 1.0,
                Evidence = _graphFactory.CreateEvidence(
                    edgeLocation,
                    handlerNode.Symbol,
                    $"Roslyn resolved MediatR handler interface '{handledMessage.HandlerInterfaceSymbol.ToDisplayString(SymbolDisplay.TypeFormat)}'.")
            });
        }
    }

    private static bool IsUnresolvedType(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol is IErrorTypeSymbol || typeSymbol.TypeKind == TypeKind.Error;
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

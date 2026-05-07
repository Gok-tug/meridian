using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal sealed class ConditionalFlowAnalyzer
{
    private readonly RoslynSourceFilter _sourceFilter;
    private readonly RoslynGraphFactory _graphFactory;

    public ConditionalFlowAnalyzer(RoslynSourceFilter sourceFilter, RoslynGraphFactory graphFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFilter);
        ArgumentNullException.ThrowIfNull(graphFactory);
        _sourceFilter = sourceFilter;
        _graphFactory = graphFactory;
    }

    public void AnalyzeIf(
        IfStatementSyntax ifStatement,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var sourceMethod = GetSourceMethod(ifStatement, semanticModel, cancellationToken);
        if (sourceMethod is null)
        {
            return;
        }

        EmitExpressionTargets(
            sourceMethod,
            ifStatement.Condition,
            GraphRelations.BranchesOn,
            "condition_text",
            ifStatement.Condition.ToString(),
            semanticModel,
            graph,
            cancellationToken);
    }

    public void AnalyzeSwitch(
        SwitchStatementSyntax switchStatement,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var sourceMethod = GetSourceMethod(switchStatement, semanticModel, cancellationToken);
        if (sourceMethod is null)
        {
            return;
        }

        EmitExpressionTargets(
            sourceMethod,
            switchStatement.Expression,
            GraphRelations.SwitchesOn,
            "switch_expression",
            switchStatement.Expression.ToString(),
            semanticModel,
            graph,
            cancellationToken);
        EmitExpressionTypeTarget(
            sourceMethod,
            switchStatement.Expression,
            GraphRelations.SwitchesOn,
            "switch_expression",
            switchStatement.Expression.ToString(),
            semanticModel,
            graph,
            cancellationToken);

        foreach (var caseLabel in switchStatement.Sections
            .SelectMany(section => section.Labels)
            .OfType<CaseSwitchLabelSyntax>()
            .OrderBy(label => label.SpanStart))
        {
            EmitExpressionTargets(
                sourceMethod,
                caseLabel.Value,
                GraphRelations.SwitchesOn,
                "case_text",
                caseLabel.Value.ToString(),
                semanticModel,
                graph,
                cancellationToken);
        }
    }

    private void EmitExpressionTargets(
        IMethodSymbol sourceMethod,
        ExpressionSyntax expression,
        string relation,
        string metadataKey,
        string metadataValue,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        foreach (var node in expression.DescendantNodesAndSelf()
            .Where(IsCandidateReference)
            .Where(node => !IsDuplicateChildReference(node))
            .OrderBy(node => node.SpanStart)
            .ThenBy(node => node.Span.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = ResolveSymbol(node, semanticModel, cancellationToken);
            if (symbol is null || TryCreateTargetNode(symbol) is not { } targetNode)
            {
                continue;
            }

            EmitConditionalEdge(
                sourceMethod,
                targetNode,
                relation,
                node.GetLocation(),
                node.ToString(),
                metadataKey,
                metadataValue,
                graph);
        }
    }

    private void EmitExpressionTypeTarget(
        IMethodSymbol sourceMethod,
        ExpressionSyntax expression,
        string relation,
        string metadataKey,
        string metadataValue,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var typeSymbol = semanticModel.GetTypeInfo(expression, cancellationToken).Type as INamedTypeSymbol;
        if (typeSymbol is not { TypeKind: TypeKind.Enum } || !_sourceFilter.HasAnalyzableSourceLocation(typeSymbol))
        {
            return;
        }

        var targetNode = _graphFactory.CreateEnumNode(typeSymbol);
        EmitConditionalEdge(
            sourceMethod,
            targetNode,
            relation,
            expression.GetLocation(),
            expression.ToString(),
            metadataKey,
            metadataValue,
            graph);
    }

    private IMethodSymbol? GetSourceMethod(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var sourceMethod = semanticModel.GetEnclosingSymbol(node.SpanStart, cancellationToken) as IMethodSymbol;
        return sourceMethod is { MethodKind: MethodKind.Ordinary } &&
            !sourceMethod.IsImplicitlyDeclared &&
            _sourceFilter.HasAnalyzableSourceLocation(sourceMethod)
            ? sourceMethod
            : null;
    }

    private static ISymbol? ResolveSymbol(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
        return symbolInfo.Symbol ?? (symbolInfo.CandidateSymbols.Length == 1 ? symbolInfo.CandidateSymbols[0] : null);
    }

    private GraphNode? TryCreateTargetNode(ISymbol symbol)
    {
        return symbol switch
        {
            IPropertySymbol propertySymbol when !propertySymbol.IsImplicitlyDeclared &&
                _sourceFilter.HasAnalyzableSourceLocation(propertySymbol) => _graphFactory.CreatePropertyNode(propertySymbol),
            IFieldSymbol fieldSymbol when fieldSymbol.ContainingType.TypeKind == TypeKind.Enum &&
                !fieldSymbol.IsImplicitlyDeclared &&
                _sourceFilter.HasAnalyzableSourceLocation(fieldSymbol) => _graphFactory.CreateEnumMemberNode(fieldSymbol),
            IFieldSymbol fieldSymbol when !fieldSymbol.IsImplicitlyDeclared &&
                _sourceFilter.HasAnalyzableSourceLocation(fieldSymbol) => _graphFactory.CreateFieldNode(fieldSymbol),
            INamedTypeSymbol { TypeKind: TypeKind.Enum } enumSymbol when
                _sourceFilter.HasAnalyzableSourceLocation(enumSymbol) => _graphFactory.CreateEnumNode(enumSymbol),
            _ => null
        };
    }

    private void EmitConditionalEdge(
        IMethodSymbol sourceMethod,
        GraphNode targetNode,
        string relation,
        Location location,
        string expression,
        string metadataKey,
        string metadataValue,
        GraphBuilder graph)
    {
        var sourceNode = _graphFactory.CreateMethodNode(sourceMethod);
        graph.AddNode(sourceNode);
        graph.AddNode(targetNode);
        graph.AddEdge(new GraphEdge
        {
            Source = sourceNode.Id,
            Target = targetNode.Id,
            Relation = relation,
            Confidence = ConfidenceLevels.Extracted,
            ConfidenceScore = 1.0,
            Evidence = _graphFactory.CreateEvidence(
                location,
                sourceNode.Symbol,
                $"Roslyn resolved conditional expression '{expression}' to '{targetNode.Symbol}'."),
            Metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                [metadataKey] = metadataValue,
                ["syntax_kind"] = location.SourceTree?.GetRoot().FindNode(location.SourceSpan).Kind().ToString() ?? string.Empty
            }
        });
    }

    private static bool IsCandidateReference(SyntaxNode node)
    {
        return node is IdentifierNameSyntax or
            GenericNameSyntax or
            QualifiedNameSyntax or
            MemberAccessExpressionSyntax or
            MemberBindingExpressionSyntax or
            ElementAccessExpressionSyntax;
    }

    private static bool IsDuplicateChildReference(SyntaxNode node)
    {
        return node.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name == node => true,
            MemberBindingExpressionSyntax memberBinding when memberBinding.Name == node => true,
            QualifiedNameSyntax qualifiedName when qualifiedName.Left == node || qualifiedName.Right == node => true,
            AliasQualifiedNameSyntax aliasQualifiedName when aliasQualifiedName.Name == node => true,
            _ => false
        };
    }
}

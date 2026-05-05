using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal sealed class MemberReferenceAnalyzer
{
    private readonly RoslynSourceFilter _sourceFilter;
    private readonly RoslynGraphFactory _graphFactory;

    public MemberReferenceAnalyzer(RoslynSourceFilter sourceFilter, RoslynGraphFactory graphFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFilter);
        ArgumentNullException.ThrowIfNull(graphFactory);
        _sourceFilter = sourceFilter;
        _graphFactory = graphFactory;
    }

    public static bool IsCandidateReference(SyntaxNode node)
    {
        return node is IdentifierNameSyntax or
            GenericNameSyntax or
            QualifiedNameSyntax or
            MemberAccessExpressionSyntax or
            MemberBindingExpressionSyntax or
            ElementAccessExpressionSyntax;
    }

    public void Analyze(
        SyntaxNode node,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        if (IsDuplicateChildReference(node))
        {
            return;
        }

        var sourceMethod = GetSourceMethod(node, semanticModel, cancellationToken);
        if (sourceMethod is null)
        {
            return;
        }

        var symbol = ResolveSymbol(node, semanticModel, cancellationToken);
        switch (symbol)
        {
            case IPropertySymbol propertySymbol:
                EmitPropertyReference(sourceMethod, propertySymbol, node, graph);
                break;
            case IFieldSymbol fieldSymbol when fieldSymbol.ContainingType.TypeKind == TypeKind.Enum:
                EmitEnumMemberReference(sourceMethod, fieldSymbol, node, graph);
                break;
            case IFieldSymbol fieldSymbol:
                EmitFieldReference(sourceMethod, fieldSymbol, node, graph);
                break;
            case INamedTypeSymbol { TypeKind: TypeKind.Enum } enumSymbol:
                EmitEnumReference(sourceMethod, enumSymbol, node, graph);
                break;
        }
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

    private void EmitPropertyReference(
        IMethodSymbol sourceMethod,
        IPropertySymbol propertySymbol,
        SyntaxNode node,
        GraphBuilder graph)
    {
        if (propertySymbol.IsImplicitlyDeclared || !_sourceFilter.HasAnalyzableSourceLocation(propertySymbol))
        {
            return;
        }

        var access = IsInsideNameofExpression(node) ? MemberAccessKind.Use : ClassifyMemberAccess(node);
        var sourceNode = _graphFactory.CreateMethodNode(sourceMethod);
        var propertyNode = _graphFactory.CreatePropertyNode(propertySymbol);
        graph.AddNode(sourceNode);
        graph.AddNode(propertyNode);
        EmitAccessEdges(sourceNode, propertyNode, node.GetLocation(), node.ToString(), access, graph);
    }

    private void EmitFieldReference(
        IMethodSymbol sourceMethod,
        IFieldSymbol fieldSymbol,
        SyntaxNode node,
        GraphBuilder graph)
    {
        if (fieldSymbol.IsImplicitlyDeclared || !_sourceFilter.HasAnalyzableSourceLocation(fieldSymbol))
        {
            return;
        }

        var access = IsInsideNameofExpression(node) ? MemberAccessKind.Use : ClassifyMemberAccess(node);
        var sourceNode = _graphFactory.CreateMethodNode(sourceMethod);
        var fieldNode = _graphFactory.CreateFieldNode(fieldSymbol);
        graph.AddNode(sourceNode);
        graph.AddNode(fieldNode);
        EmitAccessEdges(sourceNode, fieldNode, node.GetLocation(), node.ToString(), access, graph);
    }

    private void EmitEnumReference(
        IMethodSymbol sourceMethod,
        INamedTypeSymbol enumSymbol,
        SyntaxNode node,
        GraphBuilder graph)
    {
        if (!_sourceFilter.HasAnalyzableSourceLocation(enumSymbol))
        {
            return;
        }

        var sourceNode = _graphFactory.CreateMethodNode(sourceMethod);
        var enumNode = _graphFactory.CreateEnumNode(enumSymbol);
        graph.AddNode(sourceNode);
        graph.AddNode(enumNode);
        EmitUseEdge(sourceNode, enumNode, node.GetLocation(), node.ToString(), graph);
    }

    private void EmitEnumMemberReference(
        IMethodSymbol sourceMethod,
        IFieldSymbol enumMemberSymbol,
        SyntaxNode node,
        GraphBuilder graph)
    {
        if (enumMemberSymbol.IsImplicitlyDeclared || !_sourceFilter.HasAnalyzableSourceLocation(enumMemberSymbol))
        {
            return;
        }

        var sourceNode = _graphFactory.CreateMethodNode(sourceMethod);
        var enumMemberNode = _graphFactory.CreateEnumMemberNode(enumMemberSymbol);
        graph.AddNode(sourceNode);
        graph.AddNode(enumMemberNode);
        EmitUseEdge(sourceNode, enumMemberNode, node.GetLocation(), node.ToString(), graph);
    }

    private void EmitAccessEdges(
        GraphNode sourceNode,
        GraphNode targetNode,
        Location location,
        string expression,
        MemberAccessKind access,
        GraphBuilder graph)
    {
        if (access is MemberAccessKind.Use)
        {
            EmitUseEdge(sourceNode, targetNode, location, expression, graph);
            return;
        }

        if (access is MemberAccessKind.Read or MemberAccessKind.ReadWrite)
        {
            EmitMemberEdge(sourceNode, targetNode, GraphRelations.Reads, location, expression, graph);
        }

        if (access is MemberAccessKind.Write or MemberAccessKind.ReadWrite)
        {
            EmitMemberEdge(sourceNode, targetNode, GraphRelations.Writes, location, expression, graph);
        }
    }

    private void EmitUseEdge(
        GraphNode sourceNode,
        GraphNode targetNode,
        Location location,
        string expression,
        GraphBuilder graph)
    {
        EmitMemberEdge(sourceNode, targetNode, GraphRelations.Uses, location, expression, graph);
    }

    private void EmitMemberEdge(
        GraphNode sourceNode,
        GraphNode targetNode,
        string relation,
        Location location,
        string expression,
        GraphBuilder graph)
    {
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
                $"Roslyn resolved member reference '{expression}' to '{targetNode.Symbol}'.")
        });
    }

    private static MemberAccessKind ClassifyMemberAccess(SyntaxNode node)
    {
        if (TryGetRefKind(node) is { } refKind)
        {
            return refKind switch
            {
                RefKind.Out => MemberAccessKind.Write,
                RefKind.Ref => MemberAccessKind.ReadWrite,
                _ => MemberAccessKind.Read
            };
        }

        if (TryGetAssignment(node) is { } assignment)
        {
            return assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                ? MemberAccessKind.Write
                : MemberAccessKind.ReadWrite;
        }

        if (IsIncrementOrDecrementOperand(node))
        {
            return MemberAccessKind.ReadWrite;
        }

        return MemberAccessKind.Read;
    }

    private static AssignmentExpressionSyntax? TryGetAssignment(SyntaxNode node)
    {
        var current = SkipParentheses(node);
        return current.Parent is AssignmentExpressionSyntax assignment && assignment.Left == current
            ? assignment
            : null;
    }

    private static RefKind? TryGetRefKind(SyntaxNode node)
    {
        var current = SkipParentheses(node);
        if (current.Parent is not ArgumentSyntax argument || argument.Expression != current)
        {
            return null;
        }

        return argument.RefKindKeyword.Kind() switch
        {
            SyntaxKind.RefKeyword => RefKind.Ref,
            SyntaxKind.OutKeyword => RefKind.Out,
            SyntaxKind.InKeyword => RefKind.In,
            _ => null
        };
    }

    private static bool IsIncrementOrDecrementOperand(SyntaxNode node)
    {
        var current = SkipParentheses(node);
        return current.Parent switch
        {
            PrefixUnaryExpressionSyntax prefix when prefix.Operand == current &&
                (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)) => true,
            PostfixUnaryExpressionSyntax postfix when postfix.Operand == current &&
                (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression)) => true,
            _ => false
        };
    }

    private static SyntaxNode SkipParentheses(SyntaxNode node)
    {
        var current = node;
        while (current.Parent is ParenthesizedExpressionSyntax parenthesized && parenthesized.Expression == current)
        {
            current = parenthesized;
        }

        return current;
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

    private static bool IsInsideNameofExpression(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax invocation &&
                InvocationSymbolResolver.GetInvokedMethodName(invocation)?.Equals("nameof", StringComparison.Ordinal) == true)
            {
                return true;
            }
        }

        return false;
    }

    private enum MemberAccessKind
    {
        Read,
        Write,
        ReadWrite,
        Use
    }
}

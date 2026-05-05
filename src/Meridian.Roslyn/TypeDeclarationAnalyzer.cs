using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meridian.Roslyn;

internal sealed class TypeDeclarationAnalyzer
{
    private readonly RoslynSourceFilter _sourceFilter;
    private readonly RoslynGraphFactory _graphFactory;

    public TypeDeclarationAnalyzer(RoslynSourceFilter sourceFilter, RoslynGraphFactory graphFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFilter);
        ArgumentNullException.ThrowIfNull(graphFactory);
        _sourceFilter = sourceFilter;
        _graphFactory = graphFactory;
    }

    public TypeDeclarationAnalysisResult? Analyze(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) as INamedTypeSymbol;
        if (typeSymbol is null || !_sourceFilter.HasAnalyzableSourceLocation(typeSymbol))
        {
            return null;
        }

        var typeNode = _graphFactory.CreateTypeNode(typeSymbol);
        graph.AddNode(typeNode);
        AnalyzeContainedMethods(typeSymbol, typeNode, graph);
        AnalyzeContainedProperties(typeSymbol, typeNode, graph);
        AnalyzeContainedFields(typeSymbol, typeNode, graph);
        AnalyzeInterfaceImplementations(typeSymbol, typeNode, graph);

        return new TypeDeclarationAnalysisResult(typeSymbol, typeNode);
    }

    public void AnalyzeEnum(
        EnumDeclarationSyntax enumDeclaration,
        SemanticModel semanticModel,
        GraphBuilder graph,
        CancellationToken cancellationToken)
    {
        var enumSymbol = semanticModel.GetDeclaredSymbol(enumDeclaration, cancellationToken) as INamedTypeSymbol;
        if (enumSymbol is null || !_sourceFilter.HasAnalyzableSourceLocation(enumSymbol))
        {
            return;
        }

        var enumNode = _graphFactory.CreateEnumNode(enumSymbol);
        graph.AddNode(enumNode);
        AnalyzeContainedEnumMembers(enumSymbol, enumNode, graph);
    }

    private void AnalyzeContainedMethods(
        INamedTypeSymbol typeSymbol,
        GraphNode typeNode,
        GraphBuilder graph)
    {
        foreach (var method in typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary &&
                !method.IsImplicitlyDeclared &&
                method.DeclaringSyntaxReferences.Length > 0 &&
                _sourceFilter.HasAnalyzableSourceLocation(method))
            .OrderBy(method => method.ToDisplayString(SymbolDisplay.MethodFormat), StringComparer.Ordinal))
        {
            var methodNode = _graphFactory.CreateMethodNode(method);
            graph.AddNode(methodNode);

            var methodLocation = _sourceFilter.FirstAnalyzableSourceLocation(method);
            graph.AddEdge(new GraphEdge
            {
                Source = typeNode.Id,
                Target = methodNode.Id,
                Relation = GraphRelations.Contains,
                Confidence = ConfidenceLevels.Extracted,
                ConfidenceScore = 1.0,
                Evidence = _graphFactory.CreateEvidence(
                    methodLocation,
                    typeNode.Symbol,
                    $"Roslyn declared method '{methodNode.Symbol}' in type '{typeNode.Symbol}'.")
            });
        }
    }

    private void AnalyzeContainedProperties(
        INamedTypeSymbol typeSymbol,
        GraphNode typeNode,
        GraphBuilder graph)
    {
        foreach (var property in typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property => !property.IsImplicitlyDeclared &&
                property.DeclaringSyntaxReferences.Length > 0 &&
                _sourceFilter.HasAnalyzableSourceLocation(property))
            .OrderBy(property => property.ToDisplayString(SymbolDisplay.MemberFormat), StringComparer.Ordinal))
        {
            var propertyNode = _graphFactory.CreatePropertyNode(property);
            graph.AddNode(propertyNode);

            AddContainmentEdge(typeNode, propertyNode, _sourceFilter.FirstAnalyzableSourceLocation(property), "property", graph);
        }
    }

    private void AnalyzeContainedFields(
        INamedTypeSymbol typeSymbol,
        GraphNode typeNode,
        GraphBuilder graph)
    {
        foreach (var field in typeSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field => !field.IsImplicitlyDeclared &&
                field.DeclaringSyntaxReferences.Length > 0 &&
                _sourceFilter.HasAnalyzableSourceLocation(field))
            .OrderBy(field => field.ToDisplayString(SymbolDisplay.MemberFormat), StringComparer.Ordinal))
        {
            var fieldNode = _graphFactory.CreateFieldNode(field);
            graph.AddNode(fieldNode);

            AddContainmentEdge(typeNode, fieldNode, _sourceFilter.FirstAnalyzableSourceLocation(field), "field", graph);
        }
    }

    private void AnalyzeContainedEnumMembers(
        INamedTypeSymbol enumSymbol,
        GraphNode enumNode,
        GraphBuilder graph)
    {
        foreach (var enumMember in enumSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(enumMember => !enumMember.IsImplicitlyDeclared &&
                enumMember.DeclaringSyntaxReferences.Length > 0 &&
                _sourceFilter.HasAnalyzableSourceLocation(enumMember))
            .OrderBy(enumMember => enumMember.ToDisplayString(SymbolDisplay.MemberFormat), StringComparer.Ordinal))
        {
            var enumMemberNode = _graphFactory.CreateEnumMemberNode(enumMember);
            graph.AddNode(enumMemberNode);

            AddContainmentEdge(enumNode, enumMemberNode, _sourceFilter.FirstAnalyzableSourceLocation(enumMember), "enum member", graph);
        }
    }

    private void AddContainmentEdge(
        GraphNode containerNode,
        GraphNode containedNode,
        Location location,
        string containedKind,
        GraphBuilder graph)
    {
        graph.AddEdge(new GraphEdge
        {
            Source = containerNode.Id,
            Target = containedNode.Id,
            Relation = GraphRelations.Contains,
            Confidence = ConfidenceLevels.Extracted,
            ConfidenceScore = 1.0,
            Evidence = _graphFactory.CreateEvidence(
                location,
                containerNode.Symbol,
                $"Roslyn declared {containedKind} '{containedNode.Symbol}' in '{containerNode.Symbol}'.")
        });
    }

    private void AnalyzeInterfaceImplementations(
        INamedTypeSymbol typeSymbol,
        GraphNode typeNode,
        GraphBuilder graph)
    {
        foreach (var interfaceSymbol in typeSymbol.Interfaces
            .Where(_sourceFilter.HasAnalyzableSourceLocation)
            .OrderBy(interfaceSymbol => interfaceSymbol.ToDisplayString(SymbolDisplay.TypeFormat), StringComparer.Ordinal))
        {
            var interfaceNode = _graphFactory.CreateTypeNode(interfaceSymbol);
            graph.AddNode(interfaceNode);
            graph.AddEdge(new GraphEdge
            {
                Source = interfaceNode.Id,
                Target = typeNode.Id,
                Relation = GraphRelations.ImplementedBy,
                Confidence = ConfidenceLevels.Extracted,
                ConfidenceScore = 1.0,
                Evidence = _graphFactory.CreateEvidence(
                    _sourceFilter.FirstAnalyzableSourceLocation(typeSymbol),
                    typeNode.Symbol,
                    $"Roslyn resolved '{typeNode.Symbol}' as an implementation of '{interfaceNode.Symbol}'.")
            });
        }
    }
}

internal readonly record struct TypeDeclarationAnalysisResult(INamedTypeSymbol Symbol, GraphNode Node);

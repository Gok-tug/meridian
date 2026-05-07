using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;

namespace Meridian.Roslyn;

internal sealed class CommunityToolkitMvvmAnalyzer
{
    private const string ComponentModelNamespace = "CommunityToolkit.Mvvm.ComponentModel";
    private const string InputNamespace = "CommunityToolkit.Mvvm.Input";

    private readonly RoslynSourceFilter _sourceFilter;
    private readonly RoslynGraphFactory _graphFactory;

    public CommunityToolkitMvvmAnalyzer(RoslynSourceFilter sourceFilter, RoslynGraphFactory graphFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFilter);
        ArgumentNullException.ThrowIfNull(graphFactory);
        _sourceFilter = sourceFilter;
        _graphFactory = graphFactory;
    }

    public void Analyze(TypeDeclarationAnalysisResult typeResult, GraphBuilder graph)
    {
        foreach (var field in typeResult.Symbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field => !field.IsImplicitlyDeclared &&
                _sourceFilter.HasAnalyzableSourceLocation(field) &&
                HasAttribute(field, "ObservablePropertyAttribute", ComponentModelNamespace))
            .OrderBy(field => field.ToDisplayString(SymbolDisplay.MemberFormat), StringComparer.Ordinal))
        {
            AnalyzeObservableProperty(typeResult.Node, field, graph);
        }

        foreach (var method in typeResult.Symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary &&
                !method.IsImplicitlyDeclared &&
                _sourceFilter.HasAnalyzableSourceLocation(method) &&
                HasAttribute(method, "RelayCommandAttribute", InputNamespace))
            .OrderBy(method => method.ToDisplayString(SymbolDisplay.MethodFormat), StringComparer.Ordinal))
        {
            AnalyzeRelayCommand(typeResult.Node, method, graph);
        }
    }

    private void AnalyzeObservableProperty(GraphNode typeNode, IFieldSymbol fieldSymbol, GraphBuilder graph)
    {
        var propertyName = GeneratedPropertyName(fieldSymbol.Name);
        if (string.IsNullOrWhiteSpace(propertyName) || HasSourceProperty(fieldSymbol.ContainingType, propertyName))
        {
            return;
        }

        var fieldNode = _graphFactory.CreateFieldNode(fieldSymbol);
        var propertyNode = _graphFactory.CreateGeneratedPropertyNode(fieldSymbol, propertyName);
        var location = _sourceFilter.FirstAnalyzableSourceLocation(fieldSymbol);
        graph.AddNode(fieldNode);
        graph.AddNode(propertyNode);
        AddGeneratedContainment(typeNode, propertyNode, location, graph);
        AddGeneratedFrom(propertyNode, fieldNode, location, graph);
    }

    private void AnalyzeRelayCommand(GraphNode typeNode, IMethodSymbol methodSymbol, GraphBuilder graph)
    {
        var commandName = GeneratedCommandName(methodSymbol.Name);
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return;
        }

        var methodNode = _graphFactory.CreateMethodNode(methodSymbol);
        var commandNode = _graphFactory.CreateMvvmCommandNode(methodSymbol, commandName, IsAsyncCommand(methodSymbol));
        var location = _sourceFilter.FirstAnalyzableSourceLocation(methodSymbol);
        graph.AddNode(methodNode);
        graph.AddNode(commandNode);
        AddGeneratedContainment(typeNode, commandNode, location, graph);
        AddGeneratedFrom(commandNode, methodNode, location, graph);
    }

    private void AddGeneratedContainment(GraphNode typeNode, GraphNode generatedNode, Location location, GraphBuilder graph)
    {
        graph.AddEdge(new GraphEdge
        {
            Source = typeNode.Id,
            Target = generatedNode.Id,
            Relation = GraphRelations.Contains,
            Confidence = ConfidenceLevels.Inferred,
            ConfidenceScore = 0.9,
            Evidence = _graphFactory.CreateEvidence(
                location,
                typeNode.Symbol,
                $"CommunityToolkit.Mvvm source generator creates '{generatedNode.Symbol}' in '{typeNode.Symbol}'.")
        });
    }

    private void AddGeneratedFrom(GraphNode generatedNode, GraphNode sourceNode, Location location, GraphBuilder graph)
    {
        graph.AddEdge(new GraphEdge
        {
            Source = generatedNode.Id,
            Target = sourceNode.Id,
            Relation = GraphRelations.GeneratedFrom,
            Confidence = ConfidenceLevels.Inferred,
            ConfidenceScore = 0.9,
            Evidence = _graphFactory.CreateEvidence(
                location,
                sourceNode.Symbol,
                $"CommunityToolkit.Mvvm source generator creates '{generatedNode.Symbol}' from '{sourceNode.Symbol}'.")
        });
    }

    private bool HasSourceProperty(INamedTypeSymbol typeSymbol, string propertyName)
    {
        return typeSymbol.GetMembers(propertyName)
            .OfType<IPropertySymbol>()
            .Any(property => !property.IsImplicitlyDeclared && _sourceFilter.HasAnalyzableSourceLocation(property));
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName, string namespaceName)
    {
        var shortName = metadataName.EndsWith("Attribute", StringComparison.Ordinal)
            ? metadataName[..^9]
            : metadataName;
        return symbol.GetAttributes().Any(attribute =>
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null)
            {
                return false;
            }

            var nameMatches = attributeClass.Name.Equals(metadataName, StringComparison.Ordinal) ||
                attributeClass.Name.Equals(shortName, StringComparison.Ordinal);
            return nameMatches && attributeClass.ContainingNamespace.ToDisplayString().Equals(namespaceName, StringComparison.Ordinal);
        });
    }

    private static string? GeneratedPropertyName(string fieldName)
    {
        var trimmed = fieldName.StartsWith("m_", StringComparison.Ordinal) ? fieldName[2..] : fieldName.TrimStart('_');
        return trimmed.Length == 0 ? null : char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    private static string GeneratedCommandName(string methodName)
    {
        var baseName = methodName.EndsWith("Async", StringComparison.Ordinal) ? methodName[..^"Async".Length] : methodName;
        if (baseName.StartsWith("On", StringComparison.Ordinal) && baseName.Length > 2 && char.IsUpper(baseName[2]))
        {
            baseName = baseName[2..];
        }

        return baseName + "Command";
    }

    private static bool IsAsyncCommand(IMethodSymbol methodSymbol)
    {
        return methodSymbol.ReturnType is INamedTypeSymbol returnType &&
            returnType.Name is "Task" or "ValueTask" &&
            returnType.ContainingNamespace.ToDisplayString().Equals("System.Threading.Tasks", StringComparison.Ordinal);
    }
}

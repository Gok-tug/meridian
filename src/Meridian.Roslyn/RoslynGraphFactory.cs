using Meridian.Abstractions;
using Microsoft.CodeAnalysis;

namespace Meridian.Roslyn;

internal sealed class RoslynGraphFactory
{
    private readonly string _rootDirectory;
    private readonly RoslynSourceFilter _sourceFilter;

    public RoslynGraphFactory(string rootDirectory, RoslynSourceFilter sourceFilter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(sourceFilter);
        _rootDirectory = rootDirectory;
        _sourceFilter = sourceFilter;
    }

    public GraphNode CreateTypeNode(INamedTypeSymbol typeSymbol)
    {
        var location = _sourceFilter.FirstAnalyzableSourceLocation(typeSymbol);
        var symbol = typeSymbol.ToDisplayString(SymbolDisplay.TypeFormat);
        return new GraphNode
        {
            Id = $"type:{typeSymbol.ContainingAssembly.Identity.Name}:{symbol}",
            Label = typeSymbol.Name,
            Kind = GraphNodeKinds.Type,
            Symbol = symbol,
            SourceFile = SourcePath.RelativeFile(location, _rootDirectory),
            SourceLocation = SourcePath.SourceLocation(location),
            Metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["type_kind"] = typeSymbol.TypeKind.ToString().ToLowerInvariant()
            }
        };
    }

    public GraphNode CreateMethodNode(IMethodSymbol methodSymbol)
    {
        var location = _sourceFilter.FirstAnalyzableSourceLocation(methodSymbol);
        var symbol = methodSymbol.ToDisplayString(SymbolDisplay.MethodFormat);
        return new GraphNode
        {
            Id = $"method:{methodSymbol.ContainingAssembly.Identity.Name}:{symbol}",
            Label = $"{methodSymbol.ContainingType.Name}.{methodSymbol.Name}",
            Kind = GraphNodeKinds.Method,
            Symbol = symbol,
            SourceFile = SourcePath.RelativeFile(location, _rootDirectory),
            SourceLocation = SourcePath.SourceLocation(location)
        };
    }

    public GraphEvidence CreateEvidence(Location location, string? symbol, string reason)
    {
        return new GraphEvidence
        {
            File = SourcePath.RelativeFile(location, _rootDirectory),
            Line = SourcePath.Line(location),
            Symbol = symbol,
            Reason = reason
        };
    }
}

using Meridian.Abstractions;

namespace Meridian.Core;

public sealed class GraphBuilder
{
    private readonly SortedDictionary<string, GraphNode> _nodes = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, GraphEdge> _edges = new(StringComparer.Ordinal);
    private readonly List<GraphDiagnostic> _diagnostics = [];

    public void AddNode(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _nodes.TryAdd(node.Id, node);
    }

    public void AddEdge(GraphEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        var key = EdgeKey(edge);
        _edges.TryAdd(key, edge);
    }

    public void AddDiagnostic(GraphDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _diagnostics.Add(diagnostic);
    }

    public GraphDocument Build(string? root = null)
    {
        return new GraphDocument
        {
            Root = root,
            Nodes = _nodes.Values.ToArray(),
            Edges = _edges.Values.ToArray(),
            Diagnostics = _diagnostics
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .ThenBy(d => d.Message, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static string EdgeKey(GraphEdge edge)
    {
        return string.Join(
            '',
            edge.Source,
            edge.Target,
            edge.Relation,
            edge.Evidence?.File ?? string.Empty,
            edge.Evidence?.Line?.ToString("D10") ?? string.Empty,
            edge.Evidence?.Reason ?? string.Empty);
    }
}

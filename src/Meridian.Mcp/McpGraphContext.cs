using Meridian.Abstractions;
using Meridian.Core;

namespace Meridian.Mcp;

public sealed class McpGraphContext
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> _incomingByNode;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> _outgoingByNode;

    public McpGraphContext(
        GraphDocument graph,
        MeridianMcpServerOptions options,
        DateTimeOffset? loadedAt = null,
        DateTimeOffset? fileLastWriteTime = null,
        string? graphPath = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(options);
        McpGraphValidator.Validate(graph);

        Graph = graph;
        Options = options;
        LoadedAt = loadedAt ?? DateTimeOffset.UtcNow;
        FileLastWriteTime = fileLastWriteTime;
        GraphPath = graphPath ?? options.GraphPath;
        Query = new GraphQueryService(graph);
        NodesById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        _incomingByNode = graph.Edges
            .GroupBy(edge => edge.Target, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => SortEdges(group).ToArray() as IReadOnlyList<GraphEdge>, StringComparer.Ordinal);
        _outgoingByNode = graph.Edges
            .GroupBy(edge => edge.Source, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => SortEdges(group).ToArray() as IReadOnlyList<GraphEdge>, StringComparer.Ordinal);
        NodeKindsPresent = graph.Nodes
            .Select(node => node.Kind)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        RelationsPresent = graph.Edges
            .Select(edge => edge.Relation)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public GraphDocument Graph { get; }

    public MeridianMcpServerOptions Options { get; }

    public GraphQueryService Query { get; }

    public DateTimeOffset LoadedAt { get; }

    public DateTimeOffset? FileLastWriteTime { get; }

    public string GraphPath { get; }

    public IReadOnlyDictionary<string, GraphNode> NodesById { get; }

    public IReadOnlyList<string> NodeKindsPresent { get; }

    public IReadOnlyList<string> RelationsPresent { get; }

    public IReadOnlyList<GraphEdge> GetEdges(string nodeId, GraphDirection direction)
    {
        return direction switch
        {
            GraphDirection.Incoming => _incomingByNode.GetValueOrDefault(nodeId, []),
            GraphDirection.Outgoing => _outgoingByNode.GetValueOrDefault(nodeId, []),
            _ => _incomingByNode.GetValueOrDefault(nodeId, [])
                .Concat(_outgoingByNode.GetValueOrDefault(nodeId, []))
                .OrderBy(edge => edge.Source, StringComparer.Ordinal)
                .ThenBy(edge => edge.Target, StringComparer.Ordinal)
                .ThenBy(edge => edge.Relation, StringComparer.Ordinal)
                .ToArray()
        };
    }

    public int ClampMaxResults(int? maxResults)
    {
        var requested = maxResults.GetValueOrDefault(Options.DefaultMaxResults);
        if (requested <= 0)
        {
            return Options.DefaultMaxResults;
        }

        return Math.Min(requested, Options.MaxResultsLimit);
    }

    public int ClampDepth(int? depth)
    {
        var requested = depth.GetValueOrDefault(Options.DefaultMaxDepth);
        if (requested <= 0)
        {
            return Options.DefaultMaxDepth;
        }

        return Math.Min(requested, Options.MaxDepthLimit);
    }

    private static IEnumerable<GraphEdge> SortEdges(IEnumerable<GraphEdge> edges)
    {
        return edges
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.Ordinal)
            .ThenBy(edge => edge.Relation, StringComparer.Ordinal)
            .ThenBy(edge => edge.Evidence?.File, StringComparer.Ordinal)
            .ThenBy(edge => edge.Evidence?.Line)
            .ThenBy(edge => edge.Evidence?.Reason, StringComparer.Ordinal);
    }
}

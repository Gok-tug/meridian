using Meridian.Abstractions;

namespace Meridian.Core;

public sealed class GraphQueryService
{
    private readonly GraphDocument _graph;
    private readonly IReadOnlyDictionary<string, GraphNode> _nodesById;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> _incomingByNode;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> _outgoingByNode;

    public GraphQueryService(GraphDocument graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
        _nodesById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        _incomingByNode = graph.Edges
            .GroupBy(edge => edge.Target, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => SortEdges(group).ToArray() as IReadOnlyList<GraphEdge>, StringComparer.Ordinal);
        _outgoingByNode = graph.Edges
            .GroupBy(edge => edge.Source, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => SortEdges(group).ToArray() as IReadOnlyList<GraphEdge>, StringComparer.Ordinal);
    }

    public IReadOnlyList<GraphNodeMatch> FindNodes(string query, int maxResults = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), maxResults, "The maximum result count must be positive.");
        }

        return FindNodeMatches(query)
            .Take(maxResults)
            .ToArray();
    }

    public GraphNodeResolution ResolveNode(string query, int maxCandidates = int.MaxValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCandidates), maxCandidates, "The maximum candidate count must be positive.");
        }

        if (_nodesById.TryGetValue(query, out var exactIdMatch))
        {
            return GraphNodeResolution.Found(query, new GraphNodeMatch(exactIdMatch, 100));
        }

        var matches = FindNodeMatches(query).ToArray();
        if (matches.Length == 0)
        {
            return GraphNodeResolution.NotFound(query);
        }

        var topScore = matches[0].Score;
        var topMatches = matches
            .Where(match => match.Score == topScore)
            .Take(maxCandidates)
            .ToArray();

        return topMatches.Length == 1
            ? GraphNodeResolution.Found(query, topMatches[0])
            : GraphNodeResolution.Ambiguous(query, topMatches);
    }

    public GraphExplainResult? Explain(string query)
    {
        var resolution = ResolveNode(query);
        return resolution.Status == GraphNodeResolutionStatus.Found && resolution.Node is not null
            ? Explain(resolution.Node)
            : null;
    }

    public GraphExplainResult Explain(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new GraphExplainResult(
            node,
            _incomingByNode.GetValueOrDefault(node.Id, []),
            _outgoingByNode.GetValueOrDefault(node.Id, []));
    }

    public GraphPathResult? FindPath(string sourceQuery, string targetQuery)
    {
        var sourceResolution = ResolveNode(sourceQuery);
        var targetResolution = ResolveNode(targetQuery);
        return sourceResolution.Status == GraphNodeResolutionStatus.Found && sourceResolution.Node is not null &&
            targetResolution.Status == GraphNodeResolutionStatus.Found && targetResolution.Node is not null
            ? FindPath(sourceResolution.Node, targetResolution.Node)
            : null;
    }

    public GraphPathResult? FindPath(GraphNode source, GraphNode target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (source.Id == target.Id)
        {
            return new GraphPathResult(source, target, []);
        }

        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { source.Id };
        var previous = new Dictionary<string, GraphEdge>(StringComparer.Ordinal);

        queue.Enqueue(source.Id);
        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            foreach (var edge in _outgoingByNode.GetValueOrDefault(currentId, []))
            {
                if (!_nodesById.ContainsKey(edge.Target) || !visited.Add(edge.Target))
                {
                    continue;
                }

                previous[edge.Target] = edge;
                if (edge.Target == target.Id)
                {
                    return BuildPath(source, target, previous);
                }

                queue.Enqueue(edge.Target);
            }
        }

        return null;
    }

    private GraphPathResult BuildPath(GraphNode source, GraphNode target, Dictionary<string, GraphEdge> previous)
    {
        var edges = new Stack<GraphEdge>();
        var currentId = target.Id;
        while (currentId != source.Id)
        {
            var edge = previous[currentId];
            edges.Push(edge);
            currentId = edge.Source;
        }

        var segments = edges
            .Select(edge => new GraphPathSegment(_nodesById[edge.Source], edge, _nodesById[edge.Target]))
            .ToArray();

        return new GraphPathResult(source, target, segments);
    }

    private IEnumerable<GraphNodeMatch> FindNodeMatches(string query)
    {
        return _graph.Nodes
            .Select(node => new GraphNodeMatch(node, Score(node, query)))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Node.Label, StringComparer.Ordinal)
            .ThenBy(match => match.Node.Id, StringComparer.Ordinal);
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

    private static int Score(GraphNode node, string query)
    {
        if (node.Id.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (node.Label.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 90;
        }

        if (node.Symbol?.Equals(query, StringComparison.OrdinalIgnoreCase) == true)
        {
            return 80;
        }

        if (node.Id.EndsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 70;
        }

        if (node.Symbol?.EndsWith(query, StringComparison.OrdinalIgnoreCase) == true)
        {
            return 65;
        }

        if (node.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        if (node.Symbol?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
        {
            return 50;
        }

        if (node.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 40;
        }

        return 0;
    }
}

public enum GraphNodeResolutionStatus
{
    Found,
    NotFound,
    Ambiguous
}

public sealed record GraphNodeResolution(
    string Query,
    GraphNodeResolutionStatus Status,
    GraphNode? Node,
    IReadOnlyList<GraphNodeMatch> Candidates)
{
    public static GraphNodeResolution Found(string query, GraphNodeMatch match)
    {
        return new GraphNodeResolution(query, GraphNodeResolutionStatus.Found, match.Node, [match]);
    }

    public static GraphNodeResolution NotFound(string query)
    {
        return new GraphNodeResolution(query, GraphNodeResolutionStatus.NotFound, null, []);
    }

    public static GraphNodeResolution Ambiguous(string query, IReadOnlyList<GraphNodeMatch> candidates)
    {
        return new GraphNodeResolution(query, GraphNodeResolutionStatus.Ambiguous, null, candidates);
    }
}

public sealed record GraphNodeMatch(GraphNode Node, int Score);

public sealed record GraphExplainResult(
    GraphNode Node,
    IReadOnlyList<GraphEdge> IncomingEdges,
    IReadOnlyList<GraphEdge> OutgoingEdges);

public sealed record GraphPathResult(
    GraphNode Source,
    GraphNode Target,
    IReadOnlyList<GraphPathSegment> Segments);

public sealed record GraphPathSegment(GraphNode Source, GraphEdge Edge, GraphNode Target);

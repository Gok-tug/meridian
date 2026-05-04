using Meridian.Abstractions;
using Meridian.Core;
using Meridian.Mcp.Responses;

namespace Meridian.Mcp;

public sealed class MeridianGraphToolService
{
    private static readonly string[] ToolNames =
    [
        "get_schema",
        "query_graph",
        "get_node",
        "get_neighbors",
        "shortest_path",
        "explain_path",
        "list_entrypoints",
        "find_flows_to_symbol"
    ];

    private static readonly string[] KnownNodeKinds =
    [
        GraphNodeKinds.Project,
        GraphNodeKinds.Type,
        GraphNodeKinds.Method,
        GraphNodeKinds.Endpoint,
        GraphNodeKinds.Diagnostic,
        GraphNodeKinds.MediatRRequest,
        GraphNodeKinds.MediatRNotification,
        GraphNodeKinds.MediatRHandler
    ];

    private static readonly string[] KnownRelations =
    [
        GraphRelations.Contains,
        GraphRelations.Calls,
        GraphRelations.Uses,
        GraphRelations.Injects,
        GraphRelations.RegisteredAs,
        GraphRelations.ImplementedBy,
        GraphRelations.HandledBy,
        GraphRelations.Sends,
        GraphRelations.Publishes
    ];

    private readonly McpGraphContext _context;

    public MeridianGraphToolService(McpGraphContext context)
    {
        _context = context;
    }

    public SchemaResponse GetSchema()
    {
        var graph = _context.Graph;
        return new SchemaResponse(
            "ok",
            MeridianMcpMessages.StaleGraphNote,
            new GraphMetadataDto(
                graph.SchemaVersion,
                graph.Generator,
                graph.GeneratorVersion,
                graph.Root,
                graph.Nodes.Count,
                graph.Edges.Count,
                graph.Diagnostics.Count),
            ToolNames,
            _context.NodeKindsPresent,
            _context.RelationsPresent,
            KnownNodeKinds,
            KnownRelations);
    }

    public GraphSearchResponse QueryGraph(
        string? text = null,
        string? nodeKind = null,
        string? relation = null,
        GraphDirection direction = GraphDirection.Both,
        string? source = null,
        string? target = null,
        int? maxResults = null)
    {
        var limit = _context.ClampMaxResults(maxResults);
        if (LooksLikeNaturalLanguageQuestion(text) && IsBlank(nodeKind) && IsBlank(relation) && IsBlank(source) && IsBlank(target))
        {
            return EmptySearch(
                "unsupported_query",
                "query_graph accepts typed filters, not a custom query language or natural-language question. Use text, nodeKind, relation, direction, source, target, and maxResults.");
        }

        var sourceResolution = ResolveOptional(source);
        if (sourceResolution is not null && sourceResolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionSearchFailure("source", sourceResolution);
        }

        var targetResolution = ResolveOptional(target);
        if (targetResolution is not null && targetResolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionSearchFailure("target", targetResolution);
        }

        var filteredNodes = _context.Graph.Nodes
            .Where(node => MatchesText(node, text))
            .Where(node => IsBlank(nodeKind) || node.Kind.Equals(nodeKind, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.Label, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        var hasEdgeFilters = !IsBlank(relation) || sourceResolution is not null || targetResolution is not null;
        if (!hasEdgeFilters)
        {
            return BuildSearchResponse(filteredNodes, [], limit);
        }

        var nodeIds = filteredNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var edges = _context.Graph.Edges
            .Where(edge => IsBlank(relation) || edge.Relation.Equals(relation, StringComparison.OrdinalIgnoreCase))
            .Where(edge => sourceResolution?.Node is null || edge.Source == sourceResolution.Node.Id)
            .Where(edge => targetResolution?.Node is null || edge.Target == targetResolution.Node.Id)
            .Where(edge => direction switch
            {
                GraphDirection.Incoming => targetResolution?.Node is not null || nodeIds.Contains(edge.Target),
                GraphDirection.Outgoing => sourceResolution?.Node is not null || nodeIds.Contains(edge.Source),
                _ => nodeIds.Count == 0 || nodeIds.Contains(edge.Source) || nodeIds.Contains(edge.Target)
            })
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.Ordinal)
            .ThenBy(edge => edge.Relation, StringComparer.Ordinal)
            .ToArray();

        var endpointNodeIds = edges
            .SelectMany(edge => new[] { edge.Source, edge.Target })
            .Distinct(StringComparer.Ordinal);
        var nodes = endpointNodeIds
            .Select(id => _context.NodesById.GetValueOrDefault(id))
            .OfType<GraphNode>()
            .OrderBy(node => node.Label, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        return BuildSearchResponse(nodes, edges, limit);
    }

    public NodeResponse GetNode(string idOrLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrLabel);

        var resolution = ResolveNodeForMcp(idOrLabel);
        return resolution.Status switch
        {
            GraphNodeResolutionStatus.Found => new NodeResponse("ok", MeridianMcpMessages.StaleGraphNote, NodeDto.From(resolution.Node!)),
            GraphNodeResolutionStatus.Ambiguous => CreateAmbiguousNodeResponse(idOrLabel, resolution),
            _ => new NodeResponse("not_found", MeridianMcpMessages.StaleGraphNote, Message: $"No node matched '{idOrLabel}'.")
        };
    }

    public GraphSearchResponse GetNeighbors(
        string idOrLabel,
        GraphDirection direction = GraphDirection.Both,
        int? depth = null,
        string? relation = null,
        int? maxResults = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrLabel);

        var resolution = ResolveNodeForMcp(idOrLabel);
        if (resolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionSearchFailure("node", resolution);
        }

        var limit = _context.ClampMaxResults(maxResults);
        var maxDepth = _context.ClampDepth(depth);
        var start = resolution.Node!;
        var visited = new HashSet<string>(StringComparer.Ordinal) { start.Id };
        var queued = new Queue<(string NodeId, int Depth)>();
        var nodes = new List<GraphNode> { start };
        var edges = new List<GraphEdge>();
        var truncated = false;

        queued.Enqueue((start.Id, 0));
        while (queued.Count > 0 && !truncated)
        {
            var current = queued.Dequeue();
            if (current.Depth >= maxDepth)
            {
                continue;
            }

            foreach (var edge in _context.GetEdges(current.NodeId, direction).Where(edge => IsBlank(relation) || edge.Relation.Equals(relation, StringComparison.OrdinalIgnoreCase)))
            {
                if (edges.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                edges.Add(edge);
                var nextNodeId = edge.Source == current.NodeId ? edge.Target : edge.Source;
                if (!_context.NodesById.TryGetValue(nextNodeId, out var nextNode) || !visited.Add(nextNodeId))
                {
                    continue;
                }

                if (nodes.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                nodes.Add(nextNode);
                queued.Enqueue((nextNodeId, current.Depth + 1));
            }
        }

        return new GraphSearchResponse(
            "ok",
            MeridianMcpMessages.StaleGraphNote,
            nodes.Select(NodeDto.From).ToArray(),
            edges.Select(edge => EdgeDto.From(edge, _context.NodesById)).ToArray(),
            truncated,
            truncated ? MeridianMcpMessages.TruncationNote(limit) : null);
    }

    public PathResponse ShortestPath(string source, string target)
    {
        return BuildPathResponse(source, target, includeEvidence: false);
    }

    public PathResponse ExplainPath(string source, string target, bool? includeEvidence = null)
    {
        return BuildPathResponse(source, target, includeEvidence.GetValueOrDefault(_context.Options.IncludeEvidenceByDefault));
    }

    public GraphSearchResponse ListEntrypoints(int? maxResults = null)
    {
        var limit = _context.ClampMaxResults(maxResults);
        var entrypoints = _context.Graph.Nodes
            .Where(IsEntrypoint)
            .OrderBy(node => node.Label, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        var response = BuildSearchResponse(entrypoints, [], limit, entrypoints.Length == 0 ? MeridianMcpMessages.EndpointAnalyzerLimit : null);
        return response with { Status = entrypoints.Length == 0 ? "no_entrypoints" : response.Status };
    }

    public GraphSearchResponse FindFlowsToSymbol(string target, int? maxDepth = null, int? maxResults = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var resolution = ResolveNodeForMcp(target);
        if (resolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionSearchFailure("target", resolution);
        }

        var limit = _context.ClampMaxResults(maxResults);
        var depthLimit = _context.ClampDepth(maxDepth);
        var targetNode = resolution.Node!;
        var visited = new HashSet<string>(StringComparer.Ordinal) { targetNode.Id };
        var queued = new Queue<(string NodeId, int Depth)>();
        var upstreamNodes = new List<GraphNode>();
        var traversalEdges = new List<GraphEdge>();
        var truncated = false;

        queued.Enqueue((targetNode.Id, 0));
        while (queued.Count > 0 && !truncated)
        {
            var current = queued.Dequeue();
            if (current.Depth >= depthLimit)
            {
                continue;
            }

            foreach (var edge in _context.GetEdges(current.NodeId, GraphDirection.Incoming))
            {
                if (traversalEdges.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                traversalEdges.Add(edge);
                if (!_context.NodesById.TryGetValue(edge.Source, out var sourceNode) || !visited.Add(edge.Source))
                {
                    continue;
                }

                if (upstreamNodes.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                upstreamNodes.Add(sourceNode);
                queued.Enqueue((edge.Source, current.Depth + 1));
            }
        }

        var upstreamEntrypoints = upstreamNodes.Where(IsEntrypoint).ToArray();
        var graphHasEntrypoints = _context.Graph.Nodes.Any(IsEntrypoint);
        var limitation = upstreamEntrypoints.Length == 0 && !graphHasEntrypoints ? MeridianMcpMessages.EndpointAnalyzerLimit : null;
        return new GraphSearchResponse(
            upstreamEntrypoints.Length == 0 ? "no_entrypoint_flows" : "ok",
            MeridianMcpMessages.StaleGraphNote,
            upstreamNodes.Select(NodeDto.From).ToArray(),
            traversalEdges.Select(edge => EdgeDto.From(edge, _context.NodesById)).ToArray(),
            truncated,
            truncated ? MeridianMcpMessages.TruncationNote(limit) : null,
            limitation);
    }

    private PathResponse BuildPathResponse(string source, string target, bool includeEvidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var sourceResolution = ResolveNodeForMcp(source);
        if (sourceResolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionPathFailure("source", sourceResolution);
        }

        var targetResolution = ResolveNodeForMcp(target);
        if (targetResolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionPathFailure("target", targetResolution);
        }

        var path = _context.Query.FindPath(sourceResolution.Node!, targetResolution.Node!);
        if (path is null)
        {
            return new PathResponse("not_found", MeridianMcpMessages.StaleGraphNote, Message: $"No path found from '{source}' to '{target}'.");
        }

        return new PathResponse("ok", MeridianMcpMessages.StaleGraphNote, ToPathDto(path, includeEvidence));
    }

    private PathDto ToPathDto(GraphPathResult path, bool includeEvidence)
    {
        var segments = path.Segments
            .Select(segment => new PathSegmentDto(
                NodeDto.From(segment.Source),
                EdgeDto.From(includeEvidence ? segment.Edge : segment.Edge with { Evidence = null }, _context.NodesById),
                NodeDto.From(segment.Target)))
            .ToArray();
        return new PathDto(NodeDto.From(path.Source), NodeDto.From(path.Target), segments, segments.Length);
    }

    private GraphSearchResponse BuildSearchResponse(IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges, int limit, string? limitation = null)
    {
        var cappedNodes = Cap(nodes.Select(NodeDto.From), limit);
        var cappedEdges = Cap(edges.Select(edge => EdgeDto.From(edge, _context.NodesById)), limit);
        var truncated = cappedNodes.Truncated || cappedEdges.Truncated;
        return new GraphSearchResponse(
            "ok",
            MeridianMcpMessages.StaleGraphNote,
            cappedNodes.Items,
            cappedEdges.Items,
            truncated,
            truncated ? MeridianMcpMessages.TruncationNote(limit) : null,
            limitation);
    }

    private static (IReadOnlyList<T> Items, bool Truncated) Cap<T>(IEnumerable<T> values, int limit)
    {
        var items = values.Take(limit + 1).ToArray();
        return items.Length > limit
            ? (items.Take(limit).ToArray(), true)
            : (items, false);
    }

    private GraphNodeResolution ResolveNodeForMcp(string query)
    {
        var limit = _context.ClampMaxResults(null);
        return _context.Query.ResolveNode(query, limit + 1);
    }

    private GraphNodeResolution? ResolveOptional(string? query)
    {
        return IsBlank(query) ? null : ResolveNodeForMcp(query!);
    }

    private NodeResponse CreateAmbiguousNodeResponse(string query, GraphNodeResolution resolution)
    {
        var candidates = CapCandidates(resolution);
        return new NodeResponse(
            "ambiguous",
            MeridianMcpMessages.StaleGraphNote,
            Candidates: candidates.Items,
            Truncated: candidates.Truncated,
            TruncationNote: candidates.Truncated ? MeridianMcpMessages.TruncationNote(_context.ClampMaxResults(null)) : null,
            Message: $"Node query '{query}' is ambiguous. Use a more precise label, symbol, or node ID.");
    }

    private (IReadOnlyList<CandidateDto> Items, bool Truncated) CapCandidates(GraphNodeResolution resolution)
    {
        var limit = _context.ClampMaxResults(null);
        return Cap(resolution.Candidates.Select(CandidateDto.From), limit);
    }

    private static bool IsEntrypoint(GraphNode node)
    {
        return node.Kind == GraphNodeKinds.Endpoint ||
            (node.Metadata.TryGetValue("entrypoint", out var value) && value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesText(GraphNode node, string? text)
    {
        return IsBlank(text) ||
            node.Id.Contains(text!, StringComparison.OrdinalIgnoreCase) ||
            node.Label.Contains(text!, StringComparison.OrdinalIgnoreCase) ||
            (node.Symbol?.Contains(text!, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool LooksLikeNaturalLanguageQuestion(string? text)
    {
        if (IsBlank(text))
        {
            return false;
        }

        var value = text!.Trim();
        return value.Contains('?') ||
            value.StartsWith("which ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("what ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("where ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("how ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("show me ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value);
    }

    private static GraphSearchResponse EmptySearch(string status, string limitation)
    {
        return new GraphSearchResponse(status, MeridianMcpMessages.StaleGraphNote, [], [], false, null, limitation);
    }

    private GraphSearchResponse ResolutionSearchFailure(string role, GraphNodeResolution resolution)
    {
        if (resolution.Status == GraphNodeResolutionStatus.Ambiguous)
        {
            var candidates = CapCandidates(resolution);
            return new GraphSearchResponse(
                "ambiguous",
                MeridianMcpMessages.StaleGraphNote,
                [],
                [],
                candidates.Truncated,
                candidates.Truncated ? MeridianMcpMessages.TruncationNote(_context.ClampMaxResults(null)) : null,
                $"{role} node query '{resolution.Query}' is ambiguous. Use a more precise label, symbol, or node ID.",
                candidates.Items);
        }

        return new GraphSearchResponse(
            "not_found",
            MeridianMcpMessages.StaleGraphNote,
            [],
            [],
            false,
            null,
            $"No {role} node matched '{resolution.Query}'.");
    }

    private PathResponse ResolutionPathFailure(string role, GraphNodeResolution resolution)
    {
        if (resolution.Status == GraphNodeResolutionStatus.Ambiguous)
        {
            var candidates = CapCandidates(resolution);
            return new PathResponse(
                "ambiguous",
                MeridianMcpMessages.StaleGraphNote,
                Candidates: candidates.Items,
                Truncated: candidates.Truncated,
                TruncationNote: candidates.Truncated ? MeridianMcpMessages.TruncationNote(_context.ClampMaxResults(null)) : null,
                Message: $"{role} node query '{resolution.Query}' is ambiguous. Use a more precise label, symbol, or node ID.");
        }

        return new PathResponse("not_found", MeridianMcpMessages.StaleGraphNote, Message: $"No {role} node matched '{resolution.Query}'.");
    }
}

using Meridian.Abstractions;
using Meridian.Core;
using Meridian.Mcp.Responses;

namespace Meridian.Mcp;

public sealed class MeridianGraphToolService
{
    private static readonly string[] ToolNames =
    [
        "get_schema",
        "reload_graph",
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

    private const char EdgeKeySeparator = '';

    private readonly McpGraphStore _store;

    public MeridianGraphToolService(McpGraphStore store)
    {
        _store = store;
    }

    public MeridianGraphToolService(McpGraphContext context)
        : this(new McpGraphStore(context))
    {
    }

    public SchemaResponse GetSchema()
    {
        var context = _store.Current;
        var graph = context.Graph;
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
            context.NodeKindsPresent,
            context.RelationsPresent,
            KnownNodeKinds,
            KnownRelations);
    }

    public async Task<ReloadGraphResponse> ReloadGraphAsync(CancellationToken cancellationToken = default)
    {
        var result = await _store.ReloadAsync(cancellationToken);
        var current = result.Current;
        return new ReloadGraphResponse(
            result.Status,
            MeridianMcpMessages.StaleGraphNote,
            current.GraphPath,
            result.Previous.Graph.Nodes.Count,
            result.Previous.Graph.Edges.Count,
            current.Graph.Nodes.Count,
            current.Graph.Edges.Count,
            current.Graph.GeneratorVersion,
            current.LoadedAt,
            current.FileLastWriteTime,
            result.Message ?? "Graph reloaded.");
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
        var context = _store.Current;
        var limit = context.ClampMaxResults(maxResults);
        if (LooksLikeNaturalLanguageQuestion(text) && IsBlank(nodeKind) && IsBlank(relation) && IsBlank(source) && IsBlank(target))
        {
            return EmptySearch(
                "unsupported_query",
                "query_graph accepts typed filters, not a custom query language or natural-language question. Use text, nodeKind, relation, direction, source, target, and maxResults.");
        }

        var sourceResolution = ResolveOptional(context, source);
        if (sourceResolution is not null && sourceResolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionSearchFailure(context, "source", sourceResolution);
        }

        var targetResolution = ResolveOptional(context, target);
        if (targetResolution is not null && targetResolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionSearchFailure(context, "target", targetResolution);
        }

        var filteredNodes = context.Graph.Nodes
            .Where(node => MatchesText(node, text))
            .Where(node => IsBlank(nodeKind) || node.Kind.Equals(nodeKind, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.Label, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        var hasEdgeFilters = !IsBlank(relation) || sourceResolution is not null || targetResolution is not null;
        if (!hasEdgeFilters)
        {
            return BuildSearchResponse(context, filteredNodes, [], limit);
        }

        var nodeIds = filteredNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var edges = context.Graph.Edges
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
            .Select(id => context.NodesById.GetValueOrDefault(id))
            .OfType<GraphNode>()
            .OrderBy(node => node.Label, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        return BuildSearchResponse(context, nodes, edges, limit);
    }

    public NodeResponse GetNode(string idOrLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrLabel);

        var context = _store.Current;
        var resolution = ResolveNodeForMcp(context, idOrLabel);
        return resolution.Status switch
        {
            GraphNodeResolutionStatus.Found => new NodeResponse("ok", MeridianMcpMessages.StaleGraphNote, NodeDto.From(resolution.Node!)),
            GraphNodeResolutionStatus.Ambiguous => CreateAmbiguousNodeResponse(context, idOrLabel, resolution),
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

        var context = _store.Current;
        var resolution = ResolveNodeForMcp(context, idOrLabel);
        if (resolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionSearchFailure(context, "node", resolution);
        }

        var limit = context.ClampMaxResults(maxResults);
        var maxDepth = context.ClampDepth(depth);
        var start = resolution.Node!;
        var visited = new HashSet<string>(StringComparer.Ordinal) { start.Id };
        var queued = new Queue<(string NodeId, int Depth)>();
        var nodes = new List<GraphNode> { start };
        var edges = new List<GraphEdge>();
        var seenEdges = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;

        queued.Enqueue((start.Id, 0));
        while (queued.Count > 0 && !truncated)
        {
            var current = queued.Dequeue();
            if (current.Depth >= maxDepth)
            {
                continue;
            }

            foreach (var edge in context.GetEdges(current.NodeId, direction).Where(edge => IsBlank(relation) || edge.Relation.Equals(relation, StringComparison.OrdinalIgnoreCase)))
            {
                if (!seenEdges.Add(EdgeKey(edge)))
                {
                    continue;
                }

                if (edges.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                edges.Add(edge);
                var nextNodeId = edge.Source == current.NodeId ? edge.Target : edge.Source;
                if (!context.NodesById.TryGetValue(nextNodeId, out var nextNode) || !visited.Add(nextNodeId))
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
            edges.Select(edge => EdgeDto.From(edge, context.NodesById)).ToArray(),
            truncated,
            truncated ? MeridianMcpMessages.TruncationNote(limit) : null);
    }

    public PathResponse ShortestPath(string source, string target)
    {
        var context = _store.Current;
        return BuildPathResponse(context, source, target, includeEvidence: false);
    }

    public PathResponse ExplainPath(string source, string target, bool? includeEvidence = null)
    {
        var context = _store.Current;
        return BuildPathResponse(context, source, target, includeEvidence.GetValueOrDefault(context.Options.IncludeEvidenceByDefault));
    }

    public GraphSearchResponse ListEntrypoints(int? maxResults = null)
    {
        var context = _store.Current;
        var limit = context.ClampMaxResults(maxResults);
        var entrypoints = context.Graph.Nodes
            .Where(IsEntrypoint)
            .OrderBy(node => node.Label, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        var response = BuildSearchResponse(context, entrypoints, [], limit, entrypoints.Length == 0 ? MeridianMcpMessages.EndpointAnalyzerLimit : null);
        return response with { Status = entrypoints.Length == 0 ? "no_entrypoints" : response.Status };
    }

    public GraphSearchResponse FindFlowsToSymbol(string target, int? maxDepth = null, int? maxResults = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var context = _store.Current;
        var resolution = ResolveNodeForMcp(context, target);
        if (resolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionSearchFailure(context, "target", resolution);
        }

        var limit = context.ClampMaxResults(maxResults);
        var depthLimit = context.ClampDepth(maxDepth);
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

            foreach (var edge in context.GetEdges(current.NodeId, GraphDirection.Incoming))
            {
                if (traversalEdges.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                traversalEdges.Add(edge);
                if (!context.NodesById.TryGetValue(edge.Source, out var sourceNode) || !visited.Add(edge.Source))
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
        var graphHasEntrypoints = context.Graph.Nodes.Any(IsEntrypoint);
        var limitation = upstreamEntrypoints.Length == 0 && !graphHasEntrypoints ? MeridianMcpMessages.EndpointAnalyzerLimit : null;
        return new GraphSearchResponse(
            upstreamEntrypoints.Length == 0 ? "no_entrypoint_flows" : "ok",
            MeridianMcpMessages.StaleGraphNote,
            upstreamNodes.Select(NodeDto.From).ToArray(),
            traversalEdges.Select(edge => EdgeDto.From(edge, context.NodesById)).ToArray(),
            truncated,
            truncated ? MeridianMcpMessages.TruncationNote(limit) : null,
            limitation);
    }

    private PathResponse BuildPathResponse(McpGraphContext context, string source, string target, bool includeEvidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var sourceResolution = ResolveNodeForMcp(context, source);
        if (sourceResolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionPathFailure(context, "source", sourceResolution);
        }

        var targetResolution = ResolveNodeForMcp(context, target);
        if (targetResolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionPathFailure(context, "target", targetResolution);
        }

        var path = context.Query.FindPath(sourceResolution.Node!, targetResolution.Node!);
        if (path is null)
        {
            return new PathResponse("not_found", MeridianMcpMessages.StaleGraphNote, Message: $"No path found from '{source}' to '{target}'.");
        }

        return new PathResponse("ok", MeridianMcpMessages.StaleGraphNote, ToPathDto(context, path, includeEvidence));
    }

    private static PathDto ToPathDto(McpGraphContext context, GraphPathResult path, bool includeEvidence)
    {
        var segments = path.Segments
            .Select(segment => new PathSegmentDto(
                NodeDto.From(segment.Source),
                EdgeDto.From(includeEvidence ? segment.Edge : segment.Edge with { Evidence = null }, context.NodesById),
                NodeDto.From(segment.Target)))
            .ToArray();
        return new PathDto(NodeDto.From(path.Source), NodeDto.From(path.Target), segments, segments.Length);
    }

    private static GraphSearchResponse BuildSearchResponse(McpGraphContext context, IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges, int limit, string? limitation = null)
    {
        var cappedNodes = Cap(nodes.Select(NodeDto.From), limit);
        var cappedEdges = Cap(edges.Select(edge => EdgeDto.From(edge, context.NodesById)), limit);
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

    private static string EdgeKey(GraphEdge edge)
    {
        return string.Join(
            EdgeKeySeparator,
            edge.Source,
            edge.Target,
            edge.Relation,
            edge.Confidence,
            edge.ConfidenceScore?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            edge.Evidence?.File ?? string.Empty,
            edge.Evidence?.Line?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            edge.Evidence?.Symbol ?? string.Empty,
            edge.Evidence?.Reason ?? string.Empty);
    }

    private static GraphNodeResolution ResolveNodeForMcp(McpGraphContext context, string query)
    {
        var limit = context.ClampMaxResults(null);
        return context.Query.ResolveNode(query, limit + 1);
    }

    private static GraphNodeResolution? ResolveOptional(McpGraphContext context, string? query)
    {
        return IsBlank(query) ? null : ResolveNodeForMcp(context, query!);
    }

    private static NodeResponse CreateAmbiguousNodeResponse(McpGraphContext context, string query, GraphNodeResolution resolution)
    {
        var candidates = CapCandidates(context, resolution);
        return new NodeResponse(
            "ambiguous",
            MeridianMcpMessages.StaleGraphNote,
            Candidates: candidates.Items,
            Truncated: candidates.Truncated,
            TruncationNote: candidates.Truncated ? MeridianMcpMessages.TruncationNote(context.ClampMaxResults(null)) : null,
            Message: $"Node query '{query}' is ambiguous. Use a more precise label, symbol, or node ID.");
    }

    private static (IReadOnlyList<CandidateDto> Items, bool Truncated) CapCandidates(McpGraphContext context, GraphNodeResolution resolution)
    {
        var limit = context.ClampMaxResults(null);
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

    private static GraphSearchResponse ResolutionSearchFailure(McpGraphContext context, string role, GraphNodeResolution resolution)
    {
        if (resolution.Status == GraphNodeResolutionStatus.Ambiguous)
        {
            var candidates = CapCandidates(context, resolution);
            return new GraphSearchResponse(
                "ambiguous",
                MeridianMcpMessages.StaleGraphNote,
                [],
                [],
                candidates.Truncated,
                candidates.Truncated ? MeridianMcpMessages.TruncationNote(context.ClampMaxResults(null)) : null,
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

    private static PathResponse ResolutionPathFailure(McpGraphContext context, string role, GraphNodeResolution resolution)
    {
        if (resolution.Status == GraphNodeResolutionStatus.Ambiguous)
        {
            var candidates = CapCandidates(context, resolution);
            return new PathResponse(
                "ambiguous",
                MeridianMcpMessages.StaleGraphNote,
                Candidates: candidates.Items,
                Truncated: candidates.Truncated,
                TruncationNote: candidates.Truncated ? MeridianMcpMessages.TruncationNote(context.ClampMaxResults(null)) : null,
                Message: $"{role} node query '{resolution.Query}' is ambiguous. Use a more precise label, symbol, or node ID.");
        }

        return new PathResponse("not_found", MeridianMcpMessages.StaleGraphNote, Message: $"No {role} node matched '{resolution.Query}'.");
    }
}

using Meridian.Abstractions;
using Meridian.Core;
using Meridian.Mcp.Responses;
using static Meridian.Mcp.McpToolHelpers;

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
        "get_graph_statistics",
        "get_agent_summary",
        "get_symbol_summary",
        "plan_feature",
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
        GraphNodeKinds.Enum,
        GraphNodeKinds.EnumMember,
        GraphNodeKinds.Property,
        GraphNodeKinds.Field,
        GraphNodeKinds.MvvmCommand,
        GraphNodeKinds.Endpoint,
        GraphNodeKinds.Diagnostic,
        GraphNodeKinds.DbContext,
        GraphNodeKinds.MediatRRequest,
        GraphNodeKinds.MediatRNotification,
        GraphNodeKinds.MediatRHandler
    ];

    private static readonly string[] KnownRelations =
    [
        GraphRelations.Contains,
        GraphRelations.Calls,
        GraphRelations.Uses,
        GraphRelations.Reads,
        GraphRelations.GeneratedFrom,
        GraphRelations.BranchesOn,
        GraphRelations.SwitchesOn,
        GraphRelations.BindsTo,
        GraphRelations.Injects,
        GraphRelations.RegisteredAs,
        GraphRelations.ImplementedBy,
        GraphRelations.HandledBy,
        GraphRelations.Sends,
        GraphRelations.Publishes,
        GraphRelations.Queries,
        GraphRelations.Writes,
        GraphRelations.Reflects
    ];

    private static readonly string[] SchemaUsageHints =
    [
        "Start with compact bulk calls; includeEvidence defaults to false for query_graph, get_neighbors, and find_flows_to_symbol.",
        "Use includeEvidence:true only when you need evidence file, line, symbol, and reason details.",
        "For broad orientation, use get_agent_summary before reading source or traversing neighbors.",
        "Use get_graph_statistics when you need compact counts, confidence breakdowns, and diagnostics.",
        "For get_neighbors on service or type nodes, use excludeRelations:[\"contains\"] to reduce declaration-containment noise.",
        "Use get_symbol_summary before broad neighbor traversal when you need compact symbol context.",
        "Use plan_feature for absent new concepts; it ranks existing extension points instead of pretending the concept exists.",
        "No node, edge, or path result means the fact is absent from the loaded Meridian graph, not proof of absence in source code.",
        "If source changed, run meridian scan and reload_graph before concluding."
    ];

    private const char EdgeKeySeparator = '\u001f';

    private readonly McpGraphStore _store;
    private readonly FeaturePlanner _featurePlanner = new();
    private readonly GraphSummaryService _summaryService = new();

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
        var statistics = GraphStatisticsBuilder.Build(graph);
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
            KnownRelations,
            statistics.NodeKindCounts,
            statistics.RelationCounts)
        {
            UsageHints = SchemaUsageHints
        };
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
            result.PreviousGraphPreserved,
            result.Message is null ? "Graph reloaded." : $"Reload failed; previous graph preserved. {result.Message}");
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
        return QueryGraphWithOptions(text, nodeKind, relation, direction, source, target, maxResults, null, null);
    }

    public GraphSearchResponse QueryGraphWithOptions(
        string? text = null,
        string? nodeKind = null,
        string? relation = null,
        GraphDirection direction = GraphDirection.Both,
        string? source = null,
        string? target = null,
        int? maxResults = null,
        bool? includeEvidence = null,
        string[]? excludeRelations = null)
    {
        var context = _store.Current;
        var limit = context.ClampMaxResults(maxResults);
        var excludedRelations = CreateRelationSet(excludeRelations);
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

        var hasPositiveEdgeFilters = !IsBlank(relation) || sourceResolution is not null || targetResolution is not null;
        var hasEdgeFilters = hasPositiveEdgeFilters || excludedRelations.Count > 0;
        if (!hasEdgeFilters)
        {
            return BuildSearchResponse(context, filteredNodes, [], limit, includeEvidence: includeEvidence.GetValueOrDefault(false));
        }

        var nodeIds = filteredNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var edges = context.Graph.Edges
            .Where(edge => MatchesRelationFilters(edge, relation, excludedRelations))
            .Where(edge => sourceResolution?.Node is null || edge.Source == sourceResolution.Node.Id)
            .Where(edge => targetResolution?.Node is null || edge.Target == targetResolution.Node.Id)
            .Where(edge => direction switch
            {
                GraphDirection.Incoming => targetResolution?.Node is not null || nodeIds.Contains(edge.Target),
                GraphDirection.Outgoing => sourceResolution?.Node is not null || nodeIds.Contains(edge.Source),
                _ => nodeIds.Contains(edge.Source) || nodeIds.Contains(edge.Target)
            })
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.Ordinal)
            .ThenBy(edge => edge.Relation, StringComparer.Ordinal)
            .ToArray();

        var endpointNodeIds = edges
            .SelectMany(edge => new[] { edge.Source, edge.Target })
            .Distinct(StringComparer.Ordinal);
        var edgeEndpointNodes = endpointNodeIds
            .Select(id => context.NodesById.GetValueOrDefault(id))
            .OfType<GraphNode>();
        var nodes = (hasPositiveEdgeFilters ? edgeEndpointNodes : filteredNodes.Concat(edgeEndpointNodes))
            .DistinctBy(node => node.Id)
            .OrderBy(node => node.Label, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        return BuildSearchResponse(context, nodes, edges, limit, includeEvidence: includeEvidence.GetValueOrDefault(false));
    }

    public NodeResponse GetNode(string? idOrLabel)
    {
        if (IsBlank(idOrLabel))
        {
            return InvalidNodeInput("idOrLabel");
        }

        var context = _store.Current;
        var resolution = ResolveNodeForMcp(context, idOrLabel!);
        return resolution.Status switch
        {
            GraphNodeResolutionStatus.Found => new NodeResponse("ok", MeridianMcpMessages.StaleGraphNote, NodeDto.From(resolution.Node!)),
            GraphNodeResolutionStatus.Ambiguous => CreateAmbiguousNodeResponse(context, idOrLabel!, resolution),
            _ => new NodeResponse("not_found", MeridianMcpMessages.StaleGraphNote, Message: $"No node in the loaded Meridian graph matched '{idOrLabel}'. This does not prove the symbol is absent from source; regenerate and reload the graph if source changed.")
        };
    }

    public GraphSearchResponse GetNeighbors(
        string? idOrLabel,
        GraphDirection direction = GraphDirection.Both,
        int? depth = null,
        string? relation = null,
        int? maxResults = null)
    {
        return GetNeighborsWithOptions(idOrLabel, direction, depth, relation, maxResults, null, null);
    }

    public GraphSearchResponse GetNeighborsWithOptions(
        string? idOrLabel,
        GraphDirection direction = GraphDirection.Both,
        int? depth = null,
        string? relation = null,
        int? maxResults = null,
        bool? includeEvidence = null,
        string[]? excludeRelations = null)
    {
        if (IsBlank(idOrLabel))
        {
            return InvalidSearchInput("idOrLabel");
        }

        var context = _store.Current;
        var resolution = ResolveNodeForMcp(context, idOrLabel!);
        if (resolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionSearchFailure(context, "node", resolution);
        }

        var limit = context.ClampMaxResults(maxResults);
        var maxDepth = context.ClampDepth(depth);
        var excludedRelations = CreateRelationSet(excludeRelations);
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

            foreach (var edge in context.GetEdges(current.NodeId, direction).Where(edge => MatchesRelationFilters(edge, relation, excludedRelations)))
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
            edges.Select(edge => EdgeDto.From(edge, context.NodesById, includeEvidence.GetValueOrDefault(false))).ToArray(),
            truncated,
            truncated ? MeridianMcpMessages.TruncationNote(limit) : null);
    }

    public GraphStatisticsResponse GetGraphStatistics(int? maxDiagnostics = null)
    {
        var context = _store.Current;
        var diagnosticLimit = context.ClampMaxResults(maxDiagnostics);
        var statistics = GraphStatisticsBuilder.Build(context.Graph, diagnosticLimit);
        return new GraphStatisticsResponse(
            "ok",
            MeridianMcpMessages.StaleGraphNote,
            statistics,
            GraphSummaryService.BuildLimitations(statistics),
            ["get_schema", "get_agent_summary budget:\"compact\""],
            statistics.DiagnosticsTruncated,
            statistics.DiagnosticsTruncated ? MeridianMcpMessages.TruncationNote(diagnosticLimit) : null);
    }

    public AgentSummaryResponse GetAgentSummary(string? budget = null, int? maxItemsPerSection = null)
    {
        if (!GraphSummaryBudgetParser.TryParse(budget, out var summaryBudget))
        {
            return new AgentSummaryResponse(
                "invalid_input",
                MeridianMcpMessages.StaleGraphNote,
                Message: "Parameter 'budget' must be compact, standard, or detailed.");
        }

        var context = _store.Current;
        int? limit = maxItemsPerSection is null ? null : context.ClampMaxResults(maxItemsPerSection);
        var summary = _summaryService.Summarize(context.Graph, new GraphSummaryOptions
        {
            Budget = summaryBudget,
            MaxItemsPerSection = limit,
            MaxDiagnostics = limit ?? 5
        });
        return new AgentSummaryResponse(
            "ok",
            MeridianMcpMessages.StaleGraphNote,
            summary.Statistics,
            summary.CentralNodes,
            summary.ExtensionPoints,
            summary.Clusters,
            summary.Limitations,
            summary.SuggestedQueries,
            summary.Truncated,
            summary.TruncationNote);
    }

    public SymbolSummaryResponse GetSymbolSummary(string? idOrLabel, int? maxResults = null)
    {
        if (IsBlank(idOrLabel))
        {
            return InvalidSymbolSummaryInput("idOrLabel");
        }

        var context = _store.Current;
        var resolution = ResolveNodeForMcp(context, idOrLabel!);
        if (resolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionSymbolSummaryFailure(context, resolution);
        }

        var limit = context.ClampMaxResults(maxResults);
        var node = resolution.Node!;
        var incoming = context.GetEdges(node.Id, GraphDirection.Incoming);
        var outgoing = context.GetEdges(node.Id, GraphDirection.Outgoing);
        var containedMethods = RelatedNodes(context, node.Id, outgoing, GraphRelations.Contains, GraphNodeKinds.Method, limit);
        var containedProperties = RelatedNodes(context, node.Id, outgoing, GraphRelations.Contains, GraphNodeKinds.Property, limit);
        var containedFields = RelatedNodes(context, node.Id, outgoing, GraphRelations.Contains, GraphNodeKinds.Field, limit);
        var containedEnumMembers = RelatedNodes(context, node.Id, outgoing, GraphRelations.Contains, GraphNodeKinds.EnumMember, limit);
        var implementedInterfaces = RelatedNodes(context, node.Id, incoming, GraphRelations.ImplementedBy, null, limit);
        var implementations = RelatedNodes(context, node.Id, outgoing, GraphRelations.ImplementedBy, null, limit);
        var diRegistrations = RelatedNodes(context, node.Id, incoming.Concat(outgoing), GraphRelations.RegisteredAs, null, limit);
        var injectionSites = RelatedNodes(context, node.Id, incoming, GraphRelations.Injects, null, limit);
        var injectedDependencies = RelatedNodes(context, node.Id, outgoing, GraphRelations.Injects, null, limit);
        var truncated = containedMethods.Truncated ||
            containedProperties.Truncated ||
            containedFields.Truncated ||
            containedEnumMembers.Truncated ||
            implementedInterfaces.Truncated ||
            implementations.Truncated ||
            diRegistrations.Truncated ||
            injectionSites.Truncated ||
            injectedDependencies.Truncated;

        return new SymbolSummaryResponse(
            "ok",
            MeridianMcpMessages.StaleGraphNote,
            NodeDto.From(node),
            CountRelations(incoming),
            CountRelations(outgoing),
            CountImportantRelations(incoming.Concat(outgoing)),
            containedMethods.Items,
            containedProperties.Items,
            containedFields.Items,
            containedEnumMembers.Items,
            implementedInterfaces.Items,
            implementations.Items,
            diRegistrations.Items,
            injectionSites.Items,
            injectedDependencies.Items,
            SuggestedSummaryQueries(node),
            Truncated: truncated,
            TruncationNote: truncated ? MeridianMcpMessages.TruncationNote(limit) : null);
    }

    public FeaturePlanResponse PlanFeature(string? goal, string[]? seedSymbols = null, string[]? terms = null, int? maxResults = null)
    {
        return _featurePlanner.Plan(_store.Current, goal, seedSymbols, terms, maxResults);
    }

    public PathResponse ShortestPath(string? source, string? target)
    {
        var context = _store.Current;
        return BuildPathResponse(context, source, target, includeEvidence: false);
    }

    public PathResponse ExplainPath(string? source, string? target, bool? includeEvidence = null)
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

    public GraphSearchResponse FindFlowsToSymbol(string? target, int? maxDepth = null, int? maxResults = null)
    {
        return FindFlowsToSymbolWithOptions(target, maxDepth, maxResults, null, null);
    }

    public GraphSearchResponse FindFlowsToSymbolWithOptions(
        string? target,
        int? maxDepth = null,
        int? maxResults = null,
        bool? includeEvidence = null,
        string[]? excludeRelations = null)
    {
        if (IsBlank(target))
        {
            return InvalidSearchInput("target");
        }

        var context = _store.Current;
        var resolution = ResolveNodeForMcp(context, target!);
        if (resolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionSearchFailure(context, "target", resolution);
        }

        var limit = context.ClampMaxResults(maxResults);
        var depthLimit = context.ClampDepth(maxDepth);
        var excludedRelations = CreateRelationSet(excludeRelations);
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

            foreach (var edge in context.GetEdges(current.NodeId, GraphDirection.Incoming).Where(edge => MatchesRelationFilters(edge, null, excludedRelations)))
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
            traversalEdges.Select(edge => EdgeDto.From(edge, context.NodesById, includeEvidence.GetValueOrDefault(false))).ToArray(),
            truncated,
            truncated ? MeridianMcpMessages.TruncationNote(limit) : null,
            limitation);
    }

    private static IReadOnlyDictionary<string, int> CountRelations(IEnumerable<GraphEdge> edges)
    {
        return edges
            .GroupBy(edge => edge.Relation, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, int> CountImportantRelations(IEnumerable<GraphEdge> edges)
    {
        var importantRelations = GraphSummaryHeuristics.ImportantRelations.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return CountRelations(edges.Where(edge => importantRelations.Contains(edge.Relation)));
    }

    private static (IReadOnlyList<NodeDto> Items, bool Truncated) RelatedNodes(
        McpGraphContext context,
        string centerNodeId,
        IEnumerable<GraphEdge> edges,
        string relation,
        string? nodeKind,
        int limit)
    {
        var nodes = edges
            .Where(edge => edge.Relation.Equals(relation, StringComparison.OrdinalIgnoreCase))
            .Select(edge => edge.Source == centerNodeId ? edge.Target : edge.Target == centerNodeId ? edge.Source : null)
            .OfType<string>()
            .Select(id => context.NodesById.GetValueOrDefault(id))
            .OfType<GraphNode>()
            .Where(node => nodeKind is null || node.Kind.Equals(nodeKind, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(node => node.Id)
            .OrderBy(node => node.Label, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .Select(NodeDto.From);
        return Cap(nodes, limit);
    }

    private static IReadOnlyList<string> SuggestedSummaryQueries(GraphNode node)
    {
        var id = EscapeSuggestionValue(node.Id);
        return
        [
            $"get_neighbors idOrLabel:\"{id}\" direction:\"Both\" depth:1 excludeRelations:[\"contains\"]",
            $"query_graph source:\"{id}\" maxResults:20 excludeRelations:[\"contains\"]"
        ];
    }

    private static SymbolSummaryResponse InvalidSymbolSummaryInput(string parameterName)
    {
        return new SymbolSummaryResponse("invalid_input", MeridianMcpMessages.StaleGraphNote, Message: $"Parameter '{parameterName}' is required.");
    }

    private static SymbolSummaryResponse ResolutionSymbolSummaryFailure(McpGraphContext context, GraphNodeResolution resolution)
    {
        if (resolution.Status == GraphNodeResolutionStatus.Ambiguous)
        {
            var candidates = CapCandidates(context, resolution);
            return new SymbolSummaryResponse(
                "ambiguous",
                MeridianMcpMessages.StaleGraphNote,
                Candidates: candidates.Items,
                Truncated: candidates.Truncated,
                TruncationNote: candidates.Truncated ? MeridianMcpMessages.TruncationNote(context.ClampMaxResults(null)) : null,
                Message: $"Node query '{resolution.Query}' is ambiguous. Use a more precise label, symbol, or node ID.");
        }

        return new SymbolSummaryResponse(
            "not_found",
            MeridianMcpMessages.StaleGraphNote,
            Message: $"No node in the loaded Meridian graph matched '{resolution.Query}'. This does not prove the symbol is absent from source; regenerate and reload the graph if source changed.");
    }

    private PathResponse BuildPathResponse(McpGraphContext context, string? source, string? target, bool includeEvidence)
    {
        if (IsBlank(source))
        {
            return InvalidPathInput("source");
        }

        if (IsBlank(target))
        {
            return InvalidPathInput("target");
        }

        var sourceResolution = ResolveNodeForMcp(context, source!);
        if (sourceResolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionPathFailure(context, "source", sourceResolution);
        }

        var targetResolution = ResolveNodeForMcp(context, target!);
        if (targetResolution.Status != GraphNodeResolutionStatus.Found)
        {
            return ResolutionPathFailure(context, "target", targetResolution);
        }

        var path = context.Query.FindPath(sourceResolution.Node!, targetResolution.Node!);
        if (path is null)
        {
            return new PathResponse("not_found", MeridianMcpMessages.StaleGraphNote, Message: $"No path is recorded in the loaded Meridian graph from '{source}' to '{target}'. This does not prove no source-code relationship exists; regenerate and reload the graph if source changed.");
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

    private static GraphSearchResponse BuildSearchResponse(McpGraphContext context, IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges, int limit, string? limitation = null, bool includeEvidence = true)
    {
        var cappedNodes = Cap(nodes.Select(NodeDto.From), limit);
        var cappedEdges = Cap(edges.Select(edge => EdgeDto.From(edge, context.NodesById, includeEvidence)), limit);
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

    private static NodeResponse InvalidNodeInput(string parameterName)
    {
        return new NodeResponse("invalid_input", MeridianMcpMessages.StaleGraphNote, Message: $"Parameter '{parameterName}' is required.");
    }

    private static GraphSearchResponse InvalidSearchInput(string parameterName)
    {
        return new GraphSearchResponse("invalid_input", MeridianMcpMessages.StaleGraphNote, [], [], false, null, Message: $"Parameter '{parameterName}' is required.");
    }

    private static PathResponse InvalidPathInput(string parameterName)
    {
        return new PathResponse("invalid_input", MeridianMcpMessages.StaleGraphNote, Message: $"Parameter '{parameterName}' is required.");
    }

    private static HashSet<string> CreateRelationSet(IEnumerable<string>? relations)
    {
        return relations?
            .Where(relation => !IsBlank(relation))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
    }

    private static bool MatchesRelationFilters(GraphEdge edge, string? relation, HashSet<string> excludedRelations)
    {
        return (IsBlank(relation) || edge.Relation.Equals(relation, StringComparison.OrdinalIgnoreCase)) &&
            !excludedRelations.Contains(edge.Relation);
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
                Candidates: candidates.Items,
                Message: $"{role} node query '{resolution.Query}' is ambiguous. Use a more precise label, symbol, or node ID.");
        }

        return new GraphSearchResponse(
            "not_found",
            MeridianMcpMessages.StaleGraphNote,
            [],
            [],
            false,
            null,
            Message: $"No {role} node in the loaded Meridian graph matched '{resolution.Query}'. This does not prove the symbol is absent from source; regenerate and reload the graph if source changed.");
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

        return new PathResponse("not_found", MeridianMcpMessages.StaleGraphNote, Message: $"No {role} node in the loaded Meridian graph matched '{resolution.Query}'. This does not prove the symbol is absent from source; regenerate and reload the graph if source changed.");
    }
}

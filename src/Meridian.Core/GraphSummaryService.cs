using Meridian.Abstractions;

namespace Meridian.Core;

public sealed class GraphSummaryService
{
    private const int CentralDegreeScore = 3;
    private const int CentralDiversityScore = 8;
    private const int CentralImportantRelationScore = 3;
    private const int CentralNameScore = 12;
    private const int ExtensionPointNameScore = 16;
    private const int ExtensionPointRelationScore = 2;
    private const int MaximumExtensionPointRelationScore = 12;
    private const int MinimumUnnamedExtensionPointKindBoost = 14;
    private const int MinimumClusterNodeCount = 3;
    private const int MinimumClusterEdgeCount = 2;
    private const int DominantClusterPercent = 80;
    private const int HighVolumeBindsToEdgeCount = 20;

    private static readonly HashSet<string> ImportantRelations = new(GraphSummaryHeuristics.ImportantRelations, StringComparer.OrdinalIgnoreCase);

    public AgentSummaryResult Summarize(GraphDocument graph, GraphSummaryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= new GraphSummaryOptions();
        var limit = options.EffectiveMaxItemsPerSection();
        var statistics = GraphStatisticsBuilder.Build(graph, Math.Min(options.MaxDiagnostics, limit));
        var structuralNonContainmentEdges = BuildDistinctNonContainmentEdges(graph);
        var nonContainmentEdgesByNode = BuildNonContainmentEdgesByNode(structuralNonContainmentEdges);
        var centralNodes = BuildCentralNodes(graph, nonContainmentEdgesByNode, limit);
        var extensionPoints = BuildExtensionPoints(graph, nonContainmentEdgesByNode, limit);
        var clusters = BuildClusters(graph, structuralNonContainmentEdges, nonContainmentEdgesByNode, limit);
        var limitations = BuildLimitations(statistics, clusters.Items.Count == 0 ? clusters.Limitation : null);
        var suggestedQueries = BuildSuggestedQueries(centralNodes.Items, extensionPoints.Items).ToArray();
        var truncated = statistics.DiagnosticsTruncated || centralNodes.Truncated || extensionPoints.Truncated || clusters.Truncated;

        return new AgentSummaryResult(
            statistics,
            centralNodes.Items,
            extensionPoints.Items,
            clusters.Items,
            limitations,
            suggestedQueries,
            truncated,
            truncated ? $"Summary sections were capped to {limit} item(s) per section." : null);
    }

    private static (IReadOnlyList<RankedGraphNodeSummary> Items, bool Truncated) BuildCentralNodes(
        GraphDocument graph,
        IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> nonContainmentEdgesByNode,
        int limit)
    {
        var candidates = graph.Nodes
            .Select(node => ScoreCentralNode(node, nonContainmentEdgesByNode))
            .OfType<NodeSummaryCandidate>()
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Node.SourceFile, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Node.Label, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Node.Id, StringComparer.Ordinal);
        return RankAndCap(candidates, limit);
    }

    private static NodeSummaryCandidate? ScoreCentralNode(
        GraphNode node,
        IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> nonContainmentEdgesByNode)
    {
        if (!nonContainmentEdgesByNode.TryGetValue(node.Id, out var edges) || edges.Count == 0)
        {
            return null;
        }

        var relationCounts = GraphStatisticsBuilder.CountRelations(edges);
        var importantRelationCount = relationCounts
            .Where(pair => ImportantRelations.Contains(pair.Key))
            .Sum(pair => pair.Value);
        var score = edges.Count * CentralDegreeScore +
            relationCounts.Count * CentralDiversityScore +
            importantRelationCount * CentralImportantRelationScore;
        var reasons = new List<string>
        {
            $"Has {edges.Count} non-containment relation(s).",
            $"Uses {relationCounts.Count} relation kind(s)."
        };

        if (importantRelationCount > 0)
        {
            reasons.Add($"Participates in {importantRelationCount} agent-relevant relation(s).");
        }

        var kindBoost = CentralKindBoost(node, out var kindReason);
        if (kindBoost > 0)
        {
            score += kindBoost;
            reasons.Add(kindReason!);
        }

        if (GraphSummaryHeuristics.TryGetCentralNameTerm(node) is { } centralTerm)
        {
            score += CentralNameScore;
            reasons.Add($"Name suggests central abstraction: {centralTerm}.");
        }

        return new NodeSummaryCandidate(node, score, edges.Count, relationCounts.Count, relationCounts, reasons);
    }

    private static int CentralKindBoost(GraphNode node, out string? reason)
    {
        switch (node.Kind)
        {
            case GraphNodeKinds.Endpoint:
                reason = "Endpoint node is an application entrypoint.";
                return 14;
            case GraphNodeKinds.DbContext:
                reason = "DbContext node anchors persistence flow.";
                return 12;
            case GraphNodeKinds.MediatRHandler:
            case GraphNodeKinds.MediatRRequest:
            case GraphNodeKinds.MediatRNotification:
                reason = "MediatR node anchors request/notification flow.";
                return 10;
            case GraphNodeKinds.Type:
                reason = "Type node may own application behavior.";
                return 8;
            case GraphNodeKinds.Enum:
                reason = "Enum node may define application modes.";
                return 6;
            case GraphNodeKinds.Method:
                reason = "Method node participates in executable flow.";
                return 4;
            default:
                reason = null;
                return 0;
        }
    }

    private static (IReadOnlyList<RankedGraphNodeSummary> Items, bool Truncated) BuildExtensionPoints(
        GraphDocument graph,
        IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> nonContainmentEdgesByNode,
        int limit)
    {
        var candidates = graph.Nodes
            .Select(node => ScoreExtensionPoint(node, nonContainmentEdgesByNode))
            .OfType<NodeSummaryCandidate>()
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Node.SourceFile, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Node.Label, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Node.Id, StringComparer.Ordinal);
        return RankAndCap(candidates, limit);
    }

    private static NodeSummaryCandidate? ScoreExtensionPoint(
        GraphNode node,
        IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> nonContainmentEdgesByNode)
    {
        var score = 0;
        var reasons = new List<string>();
        var extensionTerm = GraphSummaryHeuristics.TryGetExtensionPointTerm(node);
        if (extensionTerm is not null)
        {
            score += ExtensionPointNameScore;
            reasons.Add($"Name suggests extension point: {extensionTerm}.");
        }

        var kindBoost = GraphSummaryHeuristics.FeaturePlanningKindBoost(node, out var kindReason);
        if (kindBoost > 0)
        {
            score += kindBoost;
            reasons.Add(kindReason!);
        }

        if (extensionTerm is null && kindBoost < MinimumUnnamedExtensionPointKindBoost)
        {
            return null;
        }

        var edges = nonContainmentEdgesByNode.GetValueOrDefault(node.Id, []);
        var relationCounts = GraphStatisticsBuilder.CountRelations(edges);
        if (edges.Count > 0)
        {
            var relationScore = Math.Min(edges.Count * ExtensionPointRelationScore, MaximumExtensionPointRelationScore);
            score += relationScore;
            reasons.Add($"Has {edges.Count} non-containment relation(s) in the graph.");
        }

        return score == 0 ? null : new NodeSummaryCandidate(node, score, edges.Count, relationCounts.Count, relationCounts, reasons);
    }

    private static (IReadOnlyList<GraphClusterSummary> Items, bool Truncated, string Limitation) BuildClusters(
        GraphDocument graph,
        IReadOnlyList<GraphEdge> structuralNonContainmentEdges,
        IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> nonContainmentEdgesByNode,
        int limit)
    {
        var nodesById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var edges = structuralNonContainmentEdges
            .Where(edge => nodesById.ContainsKey(edge.Source) && nodesById.ContainsKey(edge.Target))
            .ToArray();
        if (edges.Length < MinimumClusterEdgeCount)
        {
            return ([], false, "No distinct graph clusters were reported because the loaded graph has too few non-containment edges.");
        }

        var adjacency = BuildAdjacency(edges);
        var edgesByNode = BuildEdgesByNode(edges);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<Component>();
        foreach (var nodeId in adjacency.Keys.Order(StringComparer.Ordinal))
        {
            if (!visited.Add(nodeId))
            {
                continue;
            }

            var queue = new Queue<string>();
            var componentNodeIds = new SortedSet<string>(StringComparer.Ordinal) { nodeId };
            var componentEdges = new List<GraphEdge>();
            var componentEdgeSet = new HashSet<GraphEdge>();
            queue.Enqueue(nodeId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var edge in edgesByNode[current])
                {
                    if (componentEdgeSet.Add(edge))
                    {
                        componentEdges.Add(edge);
                    }
                }

                foreach (var next in adjacency[current].Order(StringComparer.Ordinal))
                {
                    if (visited.Add(next))
                    {
                        componentNodeIds.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }

            if (componentNodeIds.Count >= MinimumClusterNodeCount && componentEdges.Count >= MinimumClusterEdgeCount)
            {
                components.Add(new Component(
                    componentNodeIds.ToArray(),
                    componentEdges
                        .OrderBy(edge => edge.Source, StringComparer.Ordinal)
                        .ThenBy(edge => edge.Target, StringComparer.Ordinal)
                        .ThenBy(edge => edge.Relation, StringComparer.Ordinal)
                        .ToArray()));
            }
        }

        if (components.Count < 2)
        {
            return ([], false, "No distinct graph clusters were reported because the loaded graph does not have multiple separated non-containment components.");
        }

        var totalClusterNodes = components.Sum(component => component.NodeIds.Count);
        var largestClusterNodes = components.Max(component => component.NodeIds.Count);
        if (largestClusterNodes * 100 > totalClusterNodes * DominantClusterPercent)
        {
            return ([], false, "No distinct graph clusters were reported because one non-containment component dominates the loaded graph.");
        }

        var summaries = components
            .OrderByDescending(component => component.NodeIds.Count)
            .ThenByDescending(component => component.Edges.Count)
            .ThenBy(component => component.NodeIds[0], StringComparer.Ordinal)
            .Select((component, index) => BuildClusterSummary(index + 1, component, nodesById, nonContainmentEdgesByNode, limit))
            .ToArray();
        var capped = Cap(summaries, limit);
        return (capped.Items, capped.Truncated, "Graph clusters are conservative non-containment components; verify source ownership before treating them as subsystems.");
    }

    private static Dictionary<string, SortedSet<string>> BuildAdjacency(IEnumerable<GraphEdge> edges)
    {
        var adjacency = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!adjacency.TryGetValue(edge.Source, out var sourceNeighbors))
            {
                sourceNeighbors = new SortedSet<string>(StringComparer.Ordinal);
                adjacency.Add(edge.Source, sourceNeighbors);
            }

            if (!adjacency.TryGetValue(edge.Target, out var targetNeighbors))
            {
                targetNeighbors = new SortedSet<string>(StringComparer.Ordinal);
                adjacency.Add(edge.Target, targetNeighbors);
            }

            sourceNeighbors.Add(edge.Target);
            targetNeighbors.Add(edge.Source);
        }

        return adjacency;
    }

    private static Dictionary<string, List<GraphEdge>> BuildEdgesByNode(IEnumerable<GraphEdge> edges)
    {
        var edgesByNode = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!edgesByNode.TryGetValue(edge.Source, out var sourceEdges))
            {
                sourceEdges = [];
                edgesByNode.Add(edge.Source, sourceEdges);
            }

            if (!edgesByNode.TryGetValue(edge.Target, out var targetEdges))
            {
                targetEdges = [];
                edgesByNode.Add(edge.Target, targetEdges);
            }

            sourceEdges.Add(edge);
            targetEdges.Add(edge);
        }

        return edgesByNode;
    }

    private static GraphClusterSummary BuildClusterSummary(
        int rank,
        Component component,
        IReadOnlyDictionary<string, GraphNode> nodesById,
        IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> nonContainmentEdgesByNode,
        int limit)
    {
        var nodes = component.NodeIds
            .Select(id => nodesById[id])
            .ToArray();
        var representatives = nodes
            .OrderByDescending(node => nonContainmentEdgesByNode.GetValueOrDefault(node.Id, []).Count)
            .ThenBy(node => node.Label, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select(GraphNodeSummary.From)
            .ToArray();

        return new GraphClusterSummary(
            rank,
            nodes.Length,
            component.Edges.Count,
            TopCounts(nodes.Select(node => node.Kind), limit),
            TopCounts(component.Edges.Select(edge => edge.Relation), limit),
            representatives,
            "Graph cluster only; verify source ownership before treating this as a subsystem.");
    }

    public static IReadOnlyList<string> BuildLimitations(GraphStatistics statistics, string? clusterLimitation = null)
    {
        var limitations = new List<string>
        {
            "Summary is derived only from the loaded Meridian graph; source changes require meridian scan and MCP reload or restart before trusting results.",
            "Missing nodes, edges, paths, or clusters mean the fact is absent from the loaded graph, not proof of absence in source code."
        };

        if (statistics.Graph.DiagnosticCount > 0)
        {
            limitations.Add($"Loaded graph contains {statistics.Graph.DiagnosticCount} diagnostic(s); review diagnostics before relying on summary completeness.");
        }

        if (statistics.RelationCounts.TryGetValue(GraphRelations.BindsTo, out var bindsToCount) && bindsToCount >= HighVolumeBindsToEdgeCount)
        {
            limitations.Add($"Loaded graph contains {bindsToCount} binds_to edge(s); use relation:\"binds_to\" for UI binding questions.");
            limitations.Add("For non-UI architecture traversal, prefer excludeRelations:[\"contains\",\"binds_to\"] to keep MCP responses compact.");
            limitations.Add("AXAML binding facts are conservative static typed-scope facts, not runtime Avalonia binding coverage.");
        }

        if (!statistics.NodeKindCounts.ContainsKey(GraphNodeKinds.Endpoint))
        {
            limitations.Add("No endpoint nodes are present in the loaded graph; it may be stale, generated by an older Meridian version, from a non-web project, or using unsupported endpoint patterns.");
        }

        if ((statistics.NodeKindCounts.ContainsKey(GraphNodeKinds.MediatRHandler) ||
                statistics.NodeKindCounts.ContainsKey(GraphNodeKinds.MediatRRequest) ||
                statistics.NodeKindCounts.ContainsKey(GraphNodeKinds.MediatRNotification)) &&
            !statistics.RelationCounts.ContainsKey(GraphRelations.HandledBy))
        {
            limitations.Add("MediatR node kinds are present without handled_by edges; this summary cannot infer handler flow from graph facts.");
        }

        if (statistics.NodeKindCounts.ContainsKey(GraphNodeKinds.DbContext) &&
            !statistics.RelationCounts.ContainsKey(GraphRelations.Queries) &&
            !statistics.RelationCounts.ContainsKey(GraphRelations.Writes))
        {
            limitations.Add("DbContext nodes are present without queries or writes edges; this summary cannot infer persistence access from graph facts.");
        }

        if (!string.IsNullOrWhiteSpace(clusterLimitation))
        {
            limitations.Add(clusterLimitation);
        }

        return limitations;
    }

    private static IEnumerable<string> BuildSuggestedQueries(
        IReadOnlyList<RankedGraphNodeSummary> centralNodes,
        IReadOnlyList<RankedGraphNodeSummary> extensionPoints)
    {
        yield return "get_schema";
        yield return "get_graph_statistics";
        yield return "get_agent_summary budget:\"compact\"";

        foreach (var node in centralNodes.Take(2).Select(summary => summary.Node))
        {
            yield return $"get_symbol_summary idOrLabel:\"{GraphSummaryHeuristics.EscapeSuggestionValue(node.Id)}\"";
        }

        if (extensionPoints.FirstOrDefault()?.Node is { } extensionPointNode)
        {
            yield return $"plan_feature goal:\"<feature goal>\" seedSymbols:[\"{GraphSummaryHeuristics.EscapeSuggestionValue(extensionPointNode.Id)}\"]";
        }
    }

    private static (IReadOnlyList<RankedGraphNodeSummary> Items, bool Truncated) RankAndCap(IEnumerable<NodeSummaryCandidate> candidates, int limit)
    {
        var ranked = candidates
            .Select((candidate, index) => new RankedGraphNodeSummary(
                index + 1,
                candidate.Score,
                GraphNodeSummary.From(candidate.Node),
                candidate.NonContainmentDegree,
                candidate.RelationDiversity,
                candidate.RelationCounts,
                candidate.Reasons,
                SuggestedNodeQueries(candidate.Node)))
            .ToArray();
        return Cap(ranked, limit);
    }

    private static IReadOnlyList<string> SuggestedNodeQueries(GraphNode node)
    {
        var id = GraphSummaryHeuristics.EscapeSuggestionValue(node.Id);
        return
        [
            $"get_symbol_summary idOrLabel:\"{id}\"",
            $"get_neighbors idOrLabel:\"{id}\" direction:\"Both\" depth:1 excludeRelations:[\"contains\"]"
        ];
    }

    private static IReadOnlyList<GraphEdge> BuildDistinctNonContainmentEdges(GraphDocument graph)
    {
        return graph.Edges
            .Where(edge => !edge.Relation.Equals(GraphRelations.Contains, StringComparison.OrdinalIgnoreCase))
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.Ordinal)
            .ThenBy(edge => edge.Relation, StringComparer.Ordinal)
            .GroupBy(edge => (edge.Source, edge.Target, edge.Relation))
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> BuildNonContainmentEdgesByNode(IEnumerable<GraphEdge> edges)
    {
        var edgesByNode = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!edgesByNode.TryGetValue(edge.Source, out var sourceEdges))
            {
                sourceEdges = [];
                edgesByNode.Add(edge.Source, sourceEdges);
            }

            sourceEdges.Add(edge);
            if (!edgesByNode.TryGetValue(edge.Target, out var targetEdges))
            {
                targetEdges = [];
                edgesByNode.Add(edge.Target, targetEdges);
            }

            targetEdges.Add(edge);
        }

        return edgesByNode.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .OrderBy(edge => edge.Source, StringComparer.Ordinal)
                .ThenBy(edge => edge.Target, StringComparer.Ordinal)
                .ThenBy(edge => edge.Relation, StringComparer.Ordinal)
                .ToArray() as IReadOnlyList<GraphEdge>,
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, int> TopCounts(IEnumerable<string> values, int limit)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(limit)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private static (IReadOnlyList<T> Items, bool Truncated) Cap<T>(IEnumerable<T> values, int limit)
    {
        var items = values.Take(limit + 1).ToArray();
        return items.Length > limit ? (items.Take(limit).ToArray(), true) : (items, false);
    }

    private sealed record NodeSummaryCandidate(
        GraphNode Node,
        int Score,
        int NonContainmentDegree,
        int RelationDiversity,
        IReadOnlyDictionary<string, int> RelationCounts,
        IReadOnlyList<string> Reasons);

    private sealed record Component(IReadOnlyList<string> NodeIds, IReadOnlyList<GraphEdge> Edges);
}

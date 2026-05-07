using Meridian.Abstractions;
using Meridian.Core;

namespace Meridian.Core.Tests;

public sealed class GraphSummaryServiceTests
{
    [Fact]
    public void Statistics_counts_are_deterministic()
    {
        var graph = CreatePlanningGraph();

        var statistics = GraphStatisticsBuilder.Build(graph);

        Assert.Equal(6, statistics.Graph.NodeCount);
        Assert.Equal(6, statistics.Graph.EdgeCount);
        Assert.Equal(1, statistics.Graph.DiagnosticCount);
        Assert.Equal(2, statistics.NodeKindCounts[GraphNodeKinds.Method]);
        Assert.Equal(1, statistics.RelationCounts[GraphRelations.Calls]);
        Assert.Equal(6, statistics.ConfidenceCounts[ConfidenceLevels.Extracted]);
        Assert.Equal(1, statistics.DiagnosticSeverityCounts["warning"]);
        Assert.Equal("MERIDIAN_SAMPLE", statistics.TopDiagnostics.Single().Id);
    }

    [Fact]
    public void Agent_summary_ranks_central_nodes_deterministically()
    {
        var graph = CreatePlanningGraph();

        var summary = new GraphSummaryService().Summarize(graph, new GraphSummaryOptions { MaxItemsPerSection = 3 });

        Assert.Equal("TaskExecutionOrchestrator", summary.CentralNodes[0].Node.Label);
        Assert.Contains(summary.CentralNodes[0].Reasons, reason => reason.Contains("central abstraction", StringComparison.Ordinal));
        Assert.Contains(summary.CentralNodes[0].SuggestedQueries, query => query.Contains("get_symbol_summary", StringComparison.Ordinal));
    }

    [Fact]
    public void Agent_summary_uses_shared_extension_point_terms()
    {
        var graph = CreatePlanningGraph();

        var summary = new GraphSummaryService().Summarize(graph, new GraphSummaryOptions { MaxItemsPerSection = 5 });

        Assert.Contains(summary.ExtensionPoints, point => GraphSummaryHeuristics.TryGetExtensionPointTerm(point.NodeToGraphNode()) == "Strategy");
        Assert.Contains(summary.ExtensionPoints, point => point.Node.Label == "ExecutionMode");
    }

    [Fact]
    public void Weak_graph_suppresses_clusters_with_loaded_graph_limitation()
    {
        var graph = new GraphDocument
        {
            Nodes =
            [
                Node("type:A", "A", GraphNodeKinds.Type),
                Node("method:A.Run", "A.Run", GraphNodeKinds.Method)
            ],
            Edges =
            [
                Edge("type:A", "method:A.Run", GraphRelations.Contains)
            ]
        };

        var summary = new GraphSummaryService().Summarize(graph);

        Assert.Empty(summary.Clusters);
        Assert.Contains(summary.Limitations, limitation => limitation.Contains("loaded graph", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(summary.Limitations, limitation => limitation.Contains("source does not", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Separated_components_produce_conservative_clusters()
    {
        var graph = new GraphDocument
        {
            Nodes =
            [
                Node("type:A", "AService", GraphNodeKinds.Type),
                Node("method:A.One", "A.One", GraphNodeKinds.Method),
                Node("method:A.Two", "A.Two", GraphNodeKinds.Method),
                Node("type:B", "BService", GraphNodeKinds.Type),
                Node("method:B.One", "B.One", GraphNodeKinds.Method),
                Node("method:B.Two", "B.Two", GraphNodeKinds.Method)
            ],
            Edges =
            [
                Edge("type:A", "method:A.One", GraphRelations.Calls),
                Edge("method:A.One", "method:A.Two", GraphRelations.Calls),
                Edge("type:B", "method:B.One", GraphRelations.Uses),
                Edge("method:B.One", "method:B.Two", GraphRelations.Calls)
            ]
        };

        var summary = new GraphSummaryService().Summarize(graph, new GraphSummaryOptions { MaxItemsPerSection = 5 });

        Assert.Equal(2, summary.Clusters.Count);
        Assert.All(summary.Clusters, cluster => Assert.Contains("Graph cluster only", cluster.Limitation));
    }

    [Fact]
    public void Agent_summary_scores_duplicate_structural_edges_once_for_central_nodes()
    {
        var graph = new GraphDocument
        {
            Nodes =
            [
                Node("type:OrderService", "OrderService", GraphNodeKinds.Type),
                Node("method:OrderService.Handle", "OrderService.Handle", GraphNodeKinds.Method),
                Node("type:IOrderRepository", "IOrderRepository", GraphNodeKinds.Type, metadata: new() { ["type_kind"] = "interface" })
            ],
            Edges =
            [
                Edge("type:OrderService", "method:OrderService.Handle", GraphRelations.Calls),
                Edge("type:OrderService", "method:OrderService.Handle", GraphRelations.Calls, metadata: new() { ["evidence_line"] = "2" }),
                Edge("type:OrderService", "type:IOrderRepository", GraphRelations.Uses)
            ]
        };

        var summary = new GraphSummaryService().Summarize(graph, new GraphSummaryOptions { MaxItemsPerSection = 5 });
        var centralNode = Assert.Single(summary.CentralNodes, node => node.Node.Id == "type:OrderService");

        Assert.Equal(3, summary.Statistics.Graph.EdgeCount);
        Assert.Equal(2, centralNode.NonContainmentDegree);
        Assert.Equal(1, centralNode.RelationCounts[GraphRelations.Calls]);
        Assert.Equal(1, centralNode.RelationCounts[GraphRelations.Uses]);
    }

    [Fact]
    public void Agent_summary_scores_duplicate_structural_edges_once_for_extension_points()
    {
        var graph = new GraphDocument
        {
            Nodes =
            [
                Node("type:IExecutionStrategy", "IExecutionStrategy", GraphNodeKinds.Type, metadata: new() { ["type_kind"] = "interface" }),
                Node("type:FastExecutionStrategy", "FastExecutionStrategy", GraphNodeKinds.Type)
            ],
            Edges =
            [
                Edge("type:IExecutionStrategy", "type:FastExecutionStrategy", GraphRelations.ImplementedBy),
                Edge("type:IExecutionStrategy", "type:FastExecutionStrategy", GraphRelations.ImplementedBy, metadata: new() { ["evidence_line"] = "2" })
            ]
        };

        var summary = new GraphSummaryService().Summarize(graph, new GraphSummaryOptions { MaxItemsPerSection = 5 });
        var extensionPoint = Assert.Single(summary.ExtensionPoints, point => point.Node.Id == "type:IExecutionStrategy");

        Assert.Equal(2, summary.Statistics.Graph.EdgeCount);
        Assert.Equal(1, extensionPoint.NonContainmentDegree);
        Assert.Equal(1, extensionPoint.RelationCounts[GraphRelations.ImplementedBy]);
    }

    [Fact]
    public void Agent_summary_counts_binds_to_without_marking_it_agent_relevant()
    {
        var graph = new GraphDocument
        {
            Nodes =
            [
                Node("type:SettingsView", "SettingsView", GraphNodeKinds.Type),
                Node("property:SettingsViewModel.Title", "SettingsViewModel.Title", GraphNodeKinds.Property),
                Node("property:SettingsViewModel.SearchText", "SettingsViewModel.SearchText", GraphNodeKinds.Property),
                Node("mvvm_command:SettingsViewModel.SaveCommand", "SettingsViewModel.SaveCommand", GraphNodeKinds.MvvmCommand),
                Node("type:OrderService", "OrderService", GraphNodeKinds.Type),
                Node("type:OrderRepository", "OrderRepository", GraphNodeKinds.Type)
            ],
            Edges =
            [
                Edge("type:SettingsView", "property:SettingsViewModel.Title", GraphRelations.BindsTo),
                Edge("type:SettingsView", "property:SettingsViewModel.SearchText", GraphRelations.BindsTo),
                Edge("type:SettingsView", "mvvm_command:SettingsViewModel.SaveCommand", GraphRelations.BindsTo),
                Edge("type:OrderService", "type:OrderRepository", GraphRelations.Calls)
            ]
        };

        var summary = new GraphSummaryService().Summarize(graph, new GraphSummaryOptions { MaxItemsPerSection = 10 });
        var view = Assert.Single(summary.CentralNodes, node => node.Node.Id == "type:SettingsView");
        var service = Assert.Single(summary.CentralNodes, node => node.Node.Id == "type:OrderService");

        Assert.Equal(3, summary.Statistics.RelationCounts[GraphRelations.BindsTo]);
        Assert.Equal(3, view.NonContainmentDegree);
        Assert.Equal(1, view.RelationDiversity);
        Assert.Equal(3, view.RelationCounts[GraphRelations.BindsTo]);
        Assert.DoesNotContain(view.Reasons, reason => reason.Contains("agent-relevant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(service.Reasons, reason => reason.Contains("agent-relevant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Agent_summary_compact_budget_caps_sections_and_reports_truncation()
    {
        var graph = CreateLargeSummaryGraph();

        var summary = new GraphSummaryService().Summarize(graph, new GraphSummaryOptions
        {
            Budget = GraphSummaryBudget.Compact,
            MaxDiagnostics = 3
        });

        Assert.True(summary.CentralNodes.Count <= 3);
        Assert.True(summary.ExtensionPoints.Count <= 3);
        Assert.True(summary.Clusters.Count <= 3);
        Assert.Equal(3, summary.Statistics.TopDiagnostics.Count);
        Assert.True(summary.Truncated);
        Assert.Contains("capped", summary.TruncationNote, StringComparison.OrdinalIgnoreCase);
        Assert.True(summary.Statistics.DiagnosticsTruncated);
    }

    [Fact]
    public void Agent_summary_large_duplicate_edge_graph_keeps_structural_counts_bounded()
    {
        var graph = CreateLargeSummaryGraph(duplicateEdges: true);

        var summary = new GraphSummaryService().Summarize(graph, new GraphSummaryOptions { MaxItemsPerSection = 10 });
        var service0 = Assert.Single(summary.CentralNodes, node => node.Node.Id == "type:Service0");
        var service0Cluster = Assert.Single(summary.Clusters, cluster => cluster.RepresentativeNodes.Any(node => node.Id == "type:Service0"));

        Assert.True(summary.Statistics.Graph.EdgeCount > service0.NonContainmentDegree);
        Assert.Equal(2, service0.NonContainmentDegree);
        Assert.Equal(1, service0.RelationCounts[GraphRelations.ImplementedBy]);
        Assert.Equal(1, service0.RelationCounts[GraphRelations.Uses]);
        Assert.Equal(2, service0Cluster.EdgeCount);
        Assert.Equal(1, service0Cluster.TopRelations[GraphRelations.ImplementedBy]);
        Assert.Equal(1, service0Cluster.TopRelations[GraphRelations.Uses]);
    }

    [Fact]
    public void Agent_summary_scores_duplicate_structural_edges_once_for_clusters()
    {
        var graph = new GraphDocument
        {
            Nodes =
            [
                Node("type:A", "AService", GraphNodeKinds.Type),
                Node("method:A.One", "A.One", GraphNodeKinds.Method),
                Node("method:A.Two", "A.Two", GraphNodeKinds.Method),
                Node("type:B", "BService", GraphNodeKinds.Type),
                Node("method:B.One", "B.One", GraphNodeKinds.Method),
                Node("method:B.Two", "B.Two", GraphNodeKinds.Method)
            ],
            Edges =
            [
                Edge("type:A", "method:A.One", GraphRelations.Calls),
                Edge("type:A", "method:A.One", GraphRelations.Calls, metadata: new() { ["evidence_line"] = "2" }),
                Edge("method:A.One", "method:A.Two", GraphRelations.Uses),
                Edge("type:B", "method:B.One", GraphRelations.Calls),
                Edge("method:B.One", "method:B.Two", GraphRelations.Uses)
            ]
        };

        var summary = new GraphSummaryService().Summarize(graph, new GraphSummaryOptions { MaxItemsPerSection = 5 });
        var aCluster = Assert.Single(summary.Clusters, cluster =>
            cluster.RepresentativeNodes.Any(node => node.Id.StartsWith("type:A", StringComparison.Ordinal) ||
                node.Id.StartsWith("method:A", StringComparison.Ordinal)));

        Assert.Equal(5, summary.Statistics.Graph.EdgeCount);
        Assert.Equal(2, aCluster.EdgeCount);
        Assert.Equal(1, aCluster.TopRelations[GraphRelations.Calls]);
        Assert.Equal(1, aCluster.TopRelations[GraphRelations.Uses]);
    }

    private static GraphDocument CreateLargeSummaryGraph(bool duplicateEdges = false)
    {
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();
        var diagnostics = new List<GraphDiagnostic>();

        for (var component = 0; component < 4; component++)
        {
            var interfaceId = $"type:IService{component}";
            var serviceId = $"type:Service{component}";
            nodes.Add(Node(interfaceId, $"IService{component}", GraphNodeKinds.Type, new() { ["type_kind"] = "interface" }));
            nodes.Add(Node(serviceId, $"Service{component}", GraphNodeKinds.Type));
            edges.Add(Edge(interfaceId, serviceId, GraphRelations.ImplementedBy));

            for (var method = 0; method < 4; method++)
            {
                var methodId = $"method:Service{component}.Operation{method}";
                nodes.Add(Node(methodId, $"Service{component}.Operation{method}", GraphNodeKinds.Method));
                edges.Add(Edge(serviceId, methodId, GraphRelations.Contains));
                if (method > 0)
                {
                    edges.Add(Edge($"method:Service{component}.Operation{method - 1}", methodId, GraphRelations.Calls));
                    if (duplicateEdges)
                    {
                        edges.Add(Edge($"method:Service{component}.Operation{method - 1}", methodId, GraphRelations.Calls, new() { ["evidence_line"] = $"{method}" }));
                    }
                }
            }

            var dependencyId = $"type:Dependency{component}";
            nodes.Add(Node(dependencyId, $"Dependency{component}", GraphNodeKinds.Type));
            edges.Add(Edge(serviceId, dependencyId, GraphRelations.Uses));
            if (duplicateEdges)
            {
                edges.Add(Edge(serviceId, dependencyId, GraphRelations.Uses, new() { ["evidence_line"] = "99" }));
            }
        }

        for (var i = 0; i < 6; i++)
        {
            diagnostics.Add(new GraphDiagnostic
            {
                Id = $"MERIDIAN_SAMPLE_{i}",
                Severity = "warning",
                Message = $"sample diagnostic {i}"
            });
        }

        return new GraphDocument
        {
            Nodes = nodes,
            Edges = edges,
            Diagnostics = diagnostics
        };
    }

    private static GraphDocument CreatePlanningGraph()
    {
        return new GraphDocument
        {
            Nodes =
            [
                Node("method:Sample:TaskExecutionOrchestrator.ResolveExecutionStrategy()", "TaskExecutionOrchestrator.ResolveExecutionStrategy", GraphNodeKinds.Method),
                Node("enum:Sample:ExecutionMode", "ExecutionMode", GraphNodeKinds.Enum),
                Node("type:Sample:IExecutionStrategy", "IExecutionStrategy", GraphNodeKinds.Type, metadata: new() { ["type_kind"] = "interface" }),
                Node("field:Sample:TaskExecutionOrchestrator._registry", "TaskExecutionOrchestrator._registry", GraphNodeKinds.Field),
                Node("method:Sample:TaskExecutionOrchestrator.Execute()", "TaskExecutionOrchestrator.Execute", GraphNodeKinds.Method),
                Node("type:Sample:TaskExecutionOrchestrator", "TaskExecutionOrchestrator", GraphNodeKinds.Type)
            ],
            Edges =
            [
                Edge("type:Sample:TaskExecutionOrchestrator", "method:Sample:TaskExecutionOrchestrator.Execute()", GraphRelations.Contains),
                Edge("type:Sample:IExecutionStrategy", "type:Sample:TaskExecutionOrchestrator", GraphRelations.ImplementedBy),
                Edge("type:Sample:TaskExecutionOrchestrator", "field:Sample:TaskExecutionOrchestrator._registry", GraphRelations.Injects),
                Edge("method:Sample:TaskExecutionOrchestrator.Execute()", "method:Sample:TaskExecutionOrchestrator.ResolveExecutionStrategy()", GraphRelations.Calls),
                Edge("method:Sample:TaskExecutionOrchestrator.Execute()", "enum:Sample:ExecutionMode", GraphRelations.Uses),
                Edge("method:Sample:TaskExecutionOrchestrator.ResolveExecutionStrategy()", "field:Sample:TaskExecutionOrchestrator._registry", GraphRelations.Reads)
            ],
            Diagnostics =
            [
                new GraphDiagnostic
                {
                    Id = "MERIDIAN_SAMPLE",
                    Severity = "warning",
                    Message = "sample diagnostic"
                }
            ]
        };
    }

    private static GraphNode Node(string id, string label, string kind, SortedDictionary<string, string>? metadata = null)
    {
        return new GraphNode
        {
            Id = id,
            Label = label,
            Kind = kind,
            Symbol = label,
            SourceFile = "Sample.cs",
            SourceLocation = "1",
            Metadata = metadata ?? []
        };
    }

    private static GraphEdge Edge(string source, string target, string relation, SortedDictionary<string, string>? metadata = null)
    {
        return new GraphEdge
        {
            Source = source,
            Target = target,
            Relation = relation,
            Confidence = ConfidenceLevels.Extracted,
            Metadata = metadata ?? []
        };
    }
}

internal static class RankedGraphNodeSummaryTestExtensions
{
    public static GraphNode NodeToGraphNode(this RankedGraphNodeSummary summary)
    {
        return new GraphNode
        {
            Id = summary.Node.Id,
            Label = summary.Node.Label,
            Kind = summary.Node.Kind,
            Symbol = summary.Node.Symbol,
            SourceFile = summary.Node.SourceFile,
            SourceLocation = summary.Node.SourceLocation
        };
    }
}

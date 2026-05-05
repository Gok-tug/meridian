using Meridian.Abstractions;
using Meridian.Core;
using Meridian.Exporters.Json;
using Meridian.Mcp.Responses;

namespace Meridian.Mcp.Tests;

public sealed class MeridianGraphToolServiceTests
{
    [Fact]
    public void GetSchema_returns_graph_metadata_and_available_schema_values()
    {
        var service = CreateService(CreateGraph());

        var result = service.GetSchema();

        Assert.Equal("ok", result.Status);
        Assert.Contains(MeridianMcpMessages.StaleGraphNote, result.StaleGraphNote);
        Assert.Contains(GraphNodeKinds.Method, result.NodeKindsPresent);
        Assert.Contains(GraphRelations.Calls, result.RelationsPresent);
        Assert.Contains("get_schema", result.Tools);
        Assert.Contains("reload_graph", result.Tools);
        Assert.Equal(7, result.Graph.NodeCount);
    }

    [Fact]
    public async Task ReloadGraph_updates_visible_graph_from_configured_file()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);
        var service = await CreateReloadableService(graphPath);

        await JsonGraphExporter.WriteAsync(CreateReloadedGraph(), graphPath);
        var reload = await service.ReloadGraphAsync();

        Assert.Equal("ok", reload.Status);
        Assert.Equal(7, reload.PreviousNodeCount);
        Assert.Equal(1, reload.NodeCount);
        Assert.Equal(graphPath, reload.GraphPath);
        Assert.Equal("ok", service.GetNode("Reloaded.Run").Status);
        Assert.Equal("not_found", service.GetNode("Start.Run").Status);
    }

    [Fact]
    public async Task ReloadGraph_preserves_previous_graph_when_json_is_invalid()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);
        var service = await CreateReloadableService(graphPath);
        await File.WriteAllTextAsync(graphPath, "{ invalid json");

        var reload = await service.ReloadGraphAsync();

        Assert.Equal("reload_failed", reload.Status);
        Assert.Equal(7, reload.NodeCount);
        Assert.NotNull(reload.Message);
        Assert.Equal("ok", service.GetNode("Start.Run").Status);
    }

    [Fact]
    public async Task ReloadGraph_rejects_null_generator_version_and_preserves_previous_graph()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);
        var service = await CreateReloadableService(graphPath);
        await File.WriteAllTextAsync(graphPath, """
            {
              "schema_version": "0.1",
              "generator": "Meridian",
              "generator_version": null,
              "nodes": [],
              "edges": [],
              "diagnostics": []
            }
            """);

        var reload = await service.ReloadGraphAsync();

        Assert.Equal("reload_failed", reload.Status);
        Assert.Contains("generator version", reload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", service.GetNode("Start.Run").Status);
    }

    [Fact]
    public async Task ReloadGraph_rejects_null_node_metadata_and_preserves_previous_graph()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);
        var service = await CreateReloadableService(graphPath);
        await File.WriteAllTextAsync(graphPath, """
            {
              "schema_version": "0.1",
              "generator": "Meridian",
              "generator_version": "0.3.0-alpha.2",
              "nodes": [
                {
                  "id": "method:Sample:Broken.Run()",
                  "label": "Broken.Run",
                  "kind": "method",
                  "symbol": "Sample.Broken.Run()",
                  "metadata": null
                }
              ],
              "edges": [],
              "diagnostics": []
            }
            """);

        var reload = await service.ReloadGraphAsync();

        Assert.Equal("reload_failed", reload.Status);
        Assert.Contains("metadata collection", reload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", service.GetNode("Start.Run").Status);
    }

    [Fact]
    public async Task ReloadGraph_rejects_duplicate_node_ids_and_preserves_previous_graph()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);
        var service = await CreateReloadableService(graphPath);
        await JsonGraphExporter.WriteAsync(CreateDuplicateNodeIdGraph(), graphPath);

        var reload = await service.ReloadGraphAsync();

        Assert.Equal("reload_failed", reload.Status);
        Assert.Contains("duplicate node id", reload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", service.GetNode("Start.Run").Status);
    }

    [Fact]
    public async Task ReloadGraph_rejects_dangling_edges_and_preserves_previous_graph()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);
        var service = await CreateReloadableService(graphPath);
        await JsonGraphExporter.WriteAsync(CreateDanglingEdgeGraph(), graphPath);

        var reload = await service.ReloadGraphAsync();

        Assert.Equal("reload_failed", reload.Status);
        Assert.Contains("does not match a node id", reload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", service.GetNode("Start.Run").Status);
    }

    [Fact]
    public async Task ReloadGraph_swaps_complete_snapshots_without_mutating_existing_context()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);
        var options = new MeridianMcpServerOptions { GraphPath = graphPath };
        var store = await McpGraphStore.CreateAsync(options);
        var previous = store.Current;
        await JsonGraphExporter.WriteAsync(CreateReloadedGraph(), graphPath);

        var reload = await store.ReloadAsync();

        Assert.Equal("ok", reload.Status);
        Assert.Same(previous, reload.Previous);
        Assert.NotSame(previous, reload.Current);
        Assert.Same(reload.Current, store.Current);
        Assert.Equal(7, previous.Graph.Nodes.Count);
        Assert.Single(store.Current.Graph.Nodes);
    }

    [Fact]
    public void QueryGraph_uses_typed_filters_instead_of_custom_dsl()
    {
        var service = CreateService(CreateGraph());

        var result = service.QueryGraph(
            nodeKind: GraphNodeKinds.Method,
            relation: GraphRelations.Calls,
            direction: GraphDirection.Outgoing,
            source: "Start.Run");

        Assert.Equal("ok", result.Status);
        Assert.Contains(result.Edges, edge => edge.SourceLabel == "Start.Run" && edge.TargetLabel == "Middle.Run");
        Assert.Contains(result.Nodes, node => node.Label == "Start.Run");
        Assert.Contains(result.Nodes, node => node.Label == "Middle.Run");
    }

    [Fact]
    public void QueryGraph_rejects_natural_language_text()
    {
        var service = CreateService(CreateGraph());

        var result = service.QueryGraph(text: "which endpoints can reach OrderDbContext?");

        Assert.Equal("unsupported_query", result.Status);
        Assert.NotNull(result.Limitation);
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public void GetNode_returns_single_node()
    {
        var service = CreateService(CreateGraph());

        var result = service.GetNode("Start.Run");

        Assert.Equal("ok", result.Status);
        Assert.Equal("Start.Run", result.Node?.Label);
    }

    [Fact]
    public void GetNode_returns_candidates_for_ambiguous_query()
    {
        var service = CreateService(CreateGraph());

        var result = service.GetNode("Run");

        Assert.Equal("ambiguous", result.Status);
        Assert.Equal(2, result.Candidates?.Count);
        Assert.Contains(result.Candidates!, candidate => candidate.Symbol == "Sample.First.Run()");
        Assert.Contains(result.Candidates!, candidate => candidate.Symbol == "Sample.Second.Run()");
    }

    [Fact]
    public void GetNode_returns_not_found_for_missing_query()
    {
        var service = CreateService(CreateGraph());

        var result = service.GetNode("Missing");

        Assert.Equal("not_found", result.Status);
        Assert.Null(result.Node);
    }

    [Fact]
    public void GetNeighbors_caps_horizontal_results_and_returns_truncation_note()
    {
        var service = CreateService(CreateHighDegreeGraph(), new MeridianMcpServerOptions
        {
            GraphPath = "fixture.graph.json",
            DefaultMaxResults = 3,
            MaxResultsLimit = 3
        });

        var result = service.GetNeighbors("Hub.Run", GraphDirection.Outgoing, depth: 1, maxResults: 3);

        Assert.True(result.Truncated);
        Assert.Contains("TRUNCATED", result.TruncationNote);
        Assert.Equal(3, result.Edges.Count);
    }

    [Fact]
    public void GetNeighbors_both_direction_does_not_return_duplicate_edges()
    {
        var service = CreateService(CreateGraph());

        var result = service.GetNeighbors("Start.Run", GraphDirection.Both, depth: 2, maxResults: 10);

        Assert.False(result.Truncated);
        Assert.Equal(2, result.Edges.Count);
        Assert.Equal(2, result.Edges.Select(edge => $"{edge.Source}|{edge.Target}|{edge.Relation}").Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ShortestPath_returns_directed_path()
    {
        var service = CreateService(CreateGraph());

        var result = service.ShortestPath("Start.Run", "End.Run");

        Assert.Equal("ok", result.Status);
        Assert.Equal(2, result.Path?.EdgeCount);
        Assert.Equal("Start.Run", result.Path?.Segments[0].Source.Label);
        Assert.Equal("End.Run", result.Path?.Target.Label);
    }

    [Fact]
    public void ExplainPath_includes_evidence_when_requested()
    {
        var service = CreateService(CreateGraph());

        var result = service.ExplainPath("Start.Run", "End.Run", includeEvidence: true);

        Assert.Equal("ok", result.Status);
        Assert.Equal("Start calls Middle.", result.Path?.Segments[0].Edge.Evidence?.Reason);
    }

    [Fact]
    public void ListEntrypoints_returns_limitation_when_endpoint_analyzer_has_not_emitted_nodes()
    {
        var service = CreateService(CreateGraph());

        var result = service.ListEntrypoints();

        Assert.Equal("no_entrypoints", result.Status);
        Assert.Equal(MeridianMcpMessages.EndpointAnalyzerLimit, result.Limitation);
    }

    [Fact]
    public void FindFlowsToSymbol_returns_upstream_nodes_with_endpoint_limitation()
    {
        var service = CreateService(CreateGraph());

        var result = service.FindFlowsToSymbol("End.Run", maxDepth: 4);

        Assert.Equal("no_entrypoint_flows", result.Status);
        Assert.Contains(result.Nodes, node => node.Label == "Middle.Run");
        Assert.Equal(MeridianMcpMessages.EndpointAnalyzerLimit, result.Limitation);
    }

    [Fact]
    public void FindFlowsToSymbol_does_not_claim_endpoint_analyzer_limit_when_graph_has_entrypoints()
    {
        var service = CreateService(CreateGraphWithUnrelatedEndpoint());

        var result = service.FindFlowsToSymbol("End.Run", maxDepth: 4);

        Assert.Equal("no_entrypoint_flows", result.Status);
        Assert.Contains(result.Nodes, node => node.Label == "Middle.Run");
        Assert.Null(result.Limitation);
    }

    [Fact]
    public void GetNode_caps_ambiguous_candidates_and_reports_truncation()
    {
        var service = CreateService(CreateManyAmbiguousGraph(), new MeridianMcpServerOptions
        {
            GraphPath = "fixture.graph.json",
            DefaultMaxResults = 3,
            MaxResultsLimit = 3
        });

        var result = service.GetNode("Run");

        Assert.Equal("ambiguous", result.Status);
        Assert.True(result.Truncated);
        Assert.Equal(3, result.Candidates?.Count);
        Assert.Contains("TRUNCATED", result.TruncationNote);
    }

    private static MeridianGraphToolService CreateService(GraphDocument graph, MeridianMcpServerOptions? options = null)
    {
        var serverOptions = options ?? new MeridianMcpServerOptions { GraphPath = "fixture.graph.json" };
        return new MeridianGraphToolService(new McpGraphContext(graph, serverOptions));
    }

    private static async Task<MeridianGraphToolService> CreateReloadableService(string graphPath)
    {
        var store = await McpGraphStore.CreateAsync(new MeridianMcpServerOptions { GraphPath = graphPath });
        return new MeridianGraphToolService(store);
    }

    private static string CreateGraphPath()
    {
        return Path.Combine(Path.GetTempPath(), "Meridian.Mcp.Tests", Guid.NewGuid().ToString("N"), "graph.json");
    }

    private static GraphDocument CreateGraph()
    {
        var builder = new GraphBuilder();
        AddNode(builder, "method:Sample:Start.Run()", "Start.Run", "Sample.Start.Run()");
        AddNode(builder, "method:Sample:Middle.Run()", "Middle.Run", "Sample.Middle.Run()");
        AddNode(builder, "method:Sample:End.Run()", "End.Run", "Sample.End.Run()");
        AddNode(builder, "method:Sample:First.Run()", "Run", "Sample.First.Run()");
        AddNode(builder, "method:Sample:Second.Run()", "Run", "Sample.Second.Run()");
        AddNode(builder, "type:Sample:IService", "IService", "Sample.IService", GraphNodeKinds.Type);
        AddNode(builder, "type:Sample:Service", "Service", "Sample.Service", GraphNodeKinds.Type);
        AddEdge(builder, "method:Sample:Start.Run()", "method:Sample:Middle.Run()", GraphRelations.Calls, 10, "Start calls Middle.");
        AddEdge(builder, "method:Sample:Middle.Run()", "method:Sample:End.Run()", GraphRelations.Calls, 20, "Middle calls End.");
        AddEdge(builder, "type:Sample:IService", "type:Sample:Service", GraphRelations.RegisteredAs, 30, "DI registration.");
        return builder.Build(".");
    }

    private static GraphDocument CreateReloadedGraph()
    {
        var builder = new GraphBuilder();
        AddNode(builder, "method:Sample:Reloaded.Run()", "Reloaded.Run", "Sample.Reloaded.Run()");
        return builder.Build(".");
    }

    private static GraphDocument CreateDuplicateNodeIdGraph()
    {
        return new GraphDocument
        {
            Nodes =
            [
                new GraphNode { Id = "method:Sample:Duplicate.Run()", Label = "Duplicate.Run", Kind = GraphNodeKinds.Method, Symbol = "Sample.FirstDuplicate.Run()" },
                new GraphNode { Id = "method:Sample:Duplicate.Run()", Label = "Duplicate.Run", Kind = GraphNodeKinds.Method, Symbol = "Sample.SecondDuplicate.Run()" }
            ]
        };
    }

    private static GraphDocument CreateDanglingEdgeGraph()
    {
        return new GraphDocument
        {
            Nodes =
            [
                new GraphNode { Id = "method:Sample:Source.Run()", Label = "Source.Run", Kind = GraphNodeKinds.Method, Symbol = "Sample.Source.Run()" }
            ],
            Edges =
            [
                new GraphEdge
                {
                    Source = "method:Sample:Source.Run()",
                    Target = "method:Sample:Missing.Run()",
                    Relation = GraphRelations.Calls,
                    Confidence = ConfidenceLevels.Extracted,
                    ConfidenceScore = 1,
                    Evidence = new GraphEvidence { File = "Sample.cs", Line = 1, Reason = "Dangling edge." }
                }
            ]
        };
    }

    private static GraphDocument CreateGraphWithUnrelatedEndpoint()
    {
        var builder = new GraphBuilder();
        AddNode(builder, "endpoint:GET:/orders", "GET /orders", "GET /orders", GraphNodeKinds.Endpoint);
        AddNode(builder, "method:Sample:Middle.Run()", "Middle.Run", "Sample.Middle.Run()");
        AddNode(builder, "method:Sample:End.Run()", "End.Run", "Sample.End.Run()");
        AddEdge(builder, "method:Sample:Middle.Run()", "method:Sample:End.Run()", GraphRelations.Calls, 20, "Middle calls End.");
        return builder.Build(".");
    }

    private static GraphDocument CreateManyAmbiguousGraph()
    {
        var builder = new GraphBuilder();
        for (var i = 0; i < 6; i++)
        {
            AddNode(builder, $"method:Sample:Ambiguous{i}.Run()", "Run", $"Sample.Ambiguous{i}.Run()");
        }

        return builder.Build(".");
    }

    private static GraphDocument CreateHighDegreeGraph()
    {
        var builder = new GraphBuilder();
        AddNode(builder, "method:Sample:Hub.Run()", "Hub.Run", "Sample.Hub.Run()");
        for (var i = 0; i < 6; i++)
        {
            var targetId = $"method:Sample:Target{i}.Run()";
            AddNode(builder, targetId, $"Target{i}.Run", $"Sample.Target{i}.Run()");
            AddEdge(builder, "method:Sample:Hub.Run()", targetId, GraphRelations.Calls, i + 1, $"Hub calls target {i}.");
        }

        return builder.Build(".");
    }

    private static void AddNode(GraphBuilder builder, string id, string label, string symbol, string kind = GraphNodeKinds.Method)
    {
        builder.AddNode(new GraphNode { Id = id, Label = label, Kind = kind, Symbol = symbol });
    }

    private static void AddEdge(GraphBuilder builder, string source, string target, string relation, int line, string reason)
    {
        builder.AddEdge(new GraphEdge
        {
            Source = source,
            Target = target,
            Relation = relation,
            Confidence = ConfidenceLevels.Extracted,
            ConfidenceScore = 1,
            Evidence = new GraphEvidence { File = "Sample.cs", Line = line, Reason = reason }
        });
    }
}

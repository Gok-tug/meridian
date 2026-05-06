using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        Assert.Contains(GraphNodeKinds.Enum, result.KnownNodeKinds);
        Assert.Contains(GraphRelations.Reads, result.KnownRelations);
        Assert.Contains("get_schema", result.Tools);
        Assert.Contains("reload_graph", result.Tools);
        Assert.Contains("get_graph_statistics", result.Tools);
        Assert.Contains("get_agent_summary", result.Tools);
        Assert.Contains("get_symbol_summary", result.Tools);
        Assert.Contains("plan_feature", result.Tools);
        Assert.Contains(result.UsageHints, hint => hint.Contains("includeEvidence defaults to false", StringComparison.Ordinal));
        Assert.Contains(result.UsageHints, hint => hint.Contains("get_agent_summary", StringComparison.Ordinal));
        Assert.Contains(result.UsageHints, hint => hint.Contains("get_graph_statistics", StringComparison.Ordinal));
        Assert.Contains(result.UsageHints, hint => hint.Contains("excludeRelations", StringComparison.Ordinal));
        Assert.Contains(result.UsageHints, hint => hint.Contains("not proof of absence in source code", StringComparison.Ordinal));
        Assert.Equal(7, result.Graph.NodeCount);
    }

    [Fact]
    public async Task ReloadGraph_tool_forwards_cancellation()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);
        var service = await CreateReloadableService(graphPath);
        var tools = new MeridianMcpTools(service);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.ReloadGraph(cancellation.Token));
    }

    [Fact]
    public void Tool_descriptions_keep_full_schema_note_on_get_schema_only()
    {
        var methods = typeof(MeridianMcpTools).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
            Assert.NotNull(description);
            if (method.Name == nameof(MeridianMcpTools.GetSchema))
            {
                Assert.Contains("Available node kinds include", description);
                continue;
            }

            Assert.DoesNotContain("Available node kinds include", description);
            Assert.Contains("See get_schema", description);
        }
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
        Assert.True(reload.PreviousGraphPreserved);
        Assert.Contains("previous graph preserved", reload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", service.GetNode("Start.Run").Status);
    }

    [Fact]
    public async Task ReloadGraph_rejects_empty_json_object_and_preserves_previous_graph()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);
        var service = await CreateReloadableService(graphPath);
        await File.WriteAllTextAsync(graphPath, "{}");

        var reload = await service.ReloadGraphAsync();

        Assert.Equal("reload_failed", reload.Status);
        Assert.True(reload.PreviousGraphPreserved);
        Assert.Contains("previous graph preserved", reload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("schema_version", reload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", service.GetNode("Start.Run").Status);
    }

    [Fact]
    public async Task ReloadGraph_rejects_missing_nodes_collection_and_preserves_previous_graph()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);
        var service = await CreateReloadableService(graphPath);
        await File.WriteAllTextAsync(graphPath, """
            {
              "schema_version": "0.1",
              "generator": "Meridian",
              "generator_version": "0.3.0-alpha.2",
              "edges": [],
              "diagnostics": []
            }
            """);

        var reload = await service.ReloadGraphAsync();

        Assert.Equal("reload_failed", reload.Status);
        Assert.True(reload.PreviousGraphPreserved);
        Assert.Contains("nodes", reload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", service.GetNode("Start.Run").Status);
    }

    [Fact]
    public async Task ReloadGraph_rejects_unsupported_schema_version_and_preserves_previous_graph()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);
        var service = await CreateReloadableService(graphPath);
        await File.WriteAllTextAsync(graphPath, """
            {
              "schema_version": "9.9",
              "generator": "Meridian",
              "generator_version": "0.3.0-alpha.2",
              "nodes": [],
              "edges": [],
              "diagnostics": []
            }
            """);

        var reload = await service.ReloadGraphAsync();

        Assert.Equal("reload_failed", reload.Status);
        Assert.True(reload.PreviousGraphPreserved);
        Assert.Contains("Unsupported graph schema version", reload.Message, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("generator_version", reload.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task ReloadGraph_rejects_oversized_json_file_and_preserves_previous_graph()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateReloadedGraph(), graphPath);
        var service = await CreateReloadableService(graphPath, new MeridianMcpServerOptions
        {
            GraphPath = graphPath,
            MaxGraphJsonBytes = 1_000
        });
        await File.WriteAllTextAsync(graphPath, JsonGraphExporter.Serialize(CreateGraph()) + new string(' ', 1_000));

        var reload = await service.ReloadGraphAsync();

        Assert.Equal("reload_failed", reload.Status);
        Assert.True(reload.PreviousGraphPreserved);
        Assert.Contains("exceeding configured limit", reload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", service.GetNode("Reloaded.Run").Status);
    }

    [Fact]
    public async Task ReloadGraph_rejects_node_count_limit_and_preserves_previous_graph()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateReloadedGraph(), graphPath);
        var service = await CreateReloadableService(graphPath, new MeridianMcpServerOptions
        {
            GraphPath = graphPath,
            MaxGraphNodes = 1
        });
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);

        var reload = await service.ReloadGraphAsync();

        Assert.Equal("reload_failed", reload.Status);
        Assert.True(reload.PreviousGraphPreserved);
        Assert.Contains("nodes count", reload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", service.GetNode("Reloaded.Run").Status);
    }

    [Fact]
    public async Task ReloadGraph_rejects_edge_count_limit_and_preserves_previous_graph()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateReloadedGraph(), graphPath);
        var service = await CreateReloadableService(graphPath, new MeridianMcpServerOptions
        {
            GraphPath = graphPath,
            MaxGraphEdges = 2
        });
        await JsonGraphExporter.WriteAsync(CreateGraph(), graphPath);

        var reload = await service.ReloadGraphAsync();

        Assert.Equal("reload_failed", reload.Status);
        Assert.True(reload.PreviousGraphPreserved);
        Assert.Contains("edges count", reload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", service.GetNode("Reloaded.Run").Status);
    }

    [Fact]
    public async Task ReloadGraph_rejects_diagnostic_count_limit_and_preserves_previous_graph()
    {
        var graphPath = CreateGraphPath();
        await JsonGraphExporter.WriteAsync(CreateReloadedGraph(), graphPath);
        var service = await CreateReloadableService(graphPath, new MeridianMcpServerOptions
        {
            GraphPath = graphPath,
            MaxGraphDiagnostics = 1
        });
        await JsonGraphExporter.WriteAsync(CreateDiagnosticHeavyGraph(), graphPath);

        var reload = await service.ReloadGraphAsync();

        Assert.Equal("reload_failed", reload.Status);
        Assert.True(reload.PreviousGraphPreserved);
        Assert.Contains("diagnostics count", reload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ok", service.GetNode("Reloaded.Run").Status);
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

        var result = service.QueryGraphWithOptions(
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

        var result = service.QueryGraphWithOptions(text: "which endpoints can reach OrderDbContext?");

        Assert.Equal("unsupported_query", result.Status);
        Assert.NotNull(result.Limitation);
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public void QueryGraph_omits_edge_evidence_by_default_and_includes_it_when_requested()
    {
        var service = CreateService(CreateGraph());

        var compact = service.QueryGraphWithOptions(relation: GraphRelations.Calls, source: "Start.Run");
        var explained = service.QueryGraphWithOptions(relation: GraphRelations.Calls, source: "Start.Run", includeEvidence: true);

        Assert.Null(Assert.Single(compact.Edges).Evidence);
        Assert.Equal("Start calls Middle.", Assert.Single(explained.Edges).Evidence?.Reason);
    }

    [Fact]
    public void QueryGraph_compact_payload_is_smaller_than_evidence_payload()
    {
        var service = CreateService(CreateGraph());

        var compact = service.QueryGraphWithOptions(relation: GraphRelations.Calls, source: "Start.Run");
        var explained = service.QueryGraphWithOptions(relation: GraphRelations.Calls, source: "Start.Run", includeEvidence: true);

        Assert.True(SerializedByteCount(compact) < SerializedByteCount(explained));
        AssertPayloadUnder(compact, 10_000);
    }

    [Fact]
    public void QueryGraph_excluding_contains_reduces_noisy_payload_size()
    {
        var service = CreateService(CreateContainsNoiseGraph());

        var noisy = service.QueryGraphWithOptions(source: "Service", direction: GraphDirection.Outgoing, maxResults: 10);
        var filtered = service.QueryGraphWithOptions(source: "Service", direction: GraphDirection.Outgoing, maxResults: 10, excludeRelations: [GraphRelations.Contains]);

        Assert.True(filtered.Edges.Count < noisy.Edges.Count);
        Assert.True(SerializedByteCount(filtered) < SerializedByteCount(noisy));
    }

    [Fact]
    public void QueryGraph_excludes_relations_before_capping()
    {
        var service = CreateService(CreateContainsNoiseGraph());

        var result = service.QueryGraphWithOptions(source: "Service", direction: GraphDirection.Outgoing, maxResults: 2, excludeRelations: [GraphRelations.Contains]);

        var edge = Assert.Single(result.Edges);
        Assert.Equal(GraphRelations.Injects, edge.Relation);
        Assert.Equal("ZDependency", edge.TargetLabel);
    }

    [Fact]
    public void QueryGraph_preserves_node_matches_when_only_excluding_relations()
    {
        var service = CreateService(CreateGraph());

        var result = service.QueryGraphWithOptions(nodeKind: GraphNodeKinds.Method, excludeRelations: [GraphRelations.Contains]);

        Assert.Contains(result.Nodes, node => node.Symbol == "Sample.First.Run()");
        Assert.DoesNotContain(result.Edges, edge => edge.Relation == GraphRelations.Contains);
    }

    [Fact]
    public void QueryGraph_returns_empty_edges_when_requested_node_filter_matches_no_nodes()
    {
        var service = CreateService(CreateGraph());

        var result = service.QueryGraphWithOptions(text: "NoSuchNode", relation: GraphRelations.Calls);

        Assert.Equal("ok", result.Status);
        Assert.Empty(result.Nodes);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public void Mcp_tools_return_structured_invalid_input_for_blank_required_values()
    {
        var service = CreateService(CreateGraph());

        var node = service.GetNode(" ");
        var neighbors = service.GetNeighbors(" ");
        var pathSource = service.ShortestPath("", "End.Run");
        var pathTarget = service.ExplainPath("Start.Run", "");
        var flows = service.FindFlowsToSymbol(" ");

        Assert.Equal("invalid_input", node.Status);
        Assert.NotNull(node.Message);
        Assert.Equal("invalid_input", neighbors.Status);
        Assert.NotNull(neighbors.Message);
        Assert.Equal("invalid_input", pathSource.Status);
        Assert.NotNull(pathSource.Message);
        Assert.Equal("invalid_input", pathTarget.Status);
        Assert.NotNull(pathTarget.Message);
        Assert.Equal("invalid_input", flows.Status);
        Assert.NotNull(flows.Message);
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
        Assert.Contains("loaded Meridian graph", result.Message);
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
    public void GetNeighbors_omits_edge_evidence_by_default_and_includes_it_when_requested()
    {
        var service = CreateService(CreateGraph());

        var compact = service.GetNeighbors("Start.Run", GraphDirection.Outgoing, depth: 1);
        var explained = service.GetNeighborsWithOptions("Start.Run", GraphDirection.Outgoing, depth: 1, includeEvidence: true);

        Assert.Null(Assert.Single(compact.Edges).Evidence);
        Assert.Equal("Start calls Middle.", Assert.Single(explained.Edges).Evidence?.Reason);
    }

    [Fact]
    public void GetNeighbors_compact_payload_is_smaller_than_evidence_payload()
    {
        var service = CreateService(CreateGraph());

        var compact = service.GetNeighbors("Start.Run", GraphDirection.Outgoing, depth: 1);
        var explained = service.GetNeighborsWithOptions("Start.Run", GraphDirection.Outgoing, depth: 1, includeEvidence: true);

        Assert.True(SerializedByteCount(compact) < SerializedByteCount(explained));
        AssertPayloadUnder(compact, 10_000);
    }

    [Fact]
    public void GetNeighbors_truncated_payload_stays_bounded_with_explicit_note()
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
        AssertPayloadUnder(result, 12_000);
    }

    [Fact]
    public void GetNeighbors_excludes_relations_before_capping()
    {
        var service = CreateService(CreateContainsNoiseGraph());

        var result = service.GetNeighborsWithOptions("Service", GraphDirection.Outgoing, depth: 1, maxResults: 2, excludeRelations: [GraphRelations.Contains]);

        var edge = Assert.Single(result.Edges);
        Assert.Equal(GraphRelations.Injects, edge.Relation);
        Assert.Equal("ZDependency", edge.TargetLabel);
    }

    [Fact]
    public void GetNeighbors_does_not_traverse_through_excluded_edges()
    {
        var service = CreateService(CreateExcludedTraversalGraph());

        var result = service.GetNeighborsWithOptions("Root", GraphDirection.Outgoing, depth: 2, maxResults: 10, excludeRelations: [GraphRelations.Contains]);

        Assert.Empty(result.Edges);
        Assert.DoesNotContain(result.Nodes, node => node.Label == "Leaf");
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
    public void ShortestPath_not_found_message_is_limited_to_loaded_graph()
    {
        var service = CreateService(CreateGraph());

        var result = service.ShortestPath("End.Run", "Start.Run");

        Assert.Equal("not_found", result.Status);
        Assert.Contains("loaded Meridian graph", result.Message);
        Assert.Contains("does not prove", result.Message);
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
    public void ListEntrypoints_returns_limitation_when_graph_has_no_endpoint_nodes()
    {
        var service = CreateService(CreateGraph());

        var result = service.ListEntrypoints();

        Assert.Equal("no_entrypoints", result.Status);
        Assert.Equal(MeridianMcpMessages.EndpointAnalyzerLimit, result.Limitation);
    }

    [Fact]
    public void FindFlowsToSymbol_returns_upstream_nodes_with_endpoint_coverage_note()
    {
        var service = CreateService(CreateGraph());

        var result = service.FindFlowsToSymbol("End.Run", maxDepth: 4);

        Assert.Equal("no_entrypoint_flows", result.Status);
        Assert.Contains(result.Nodes, node => node.Label == "Middle.Run");
        Assert.Equal(MeridianMcpMessages.EndpointAnalyzerLimit, result.Limitation);
    }

    [Fact]
    public void FindFlowsToSymbol_omits_edge_evidence_by_default_and_includes_it_when_requested()
    {
        var service = CreateService(CreateGraph());

        var compact = service.FindFlowsToSymbol("End.Run", maxDepth: 4);
        var explained = service.FindFlowsToSymbolWithOptions("End.Run", maxDepth: 4, includeEvidence: true);

        Assert.All(compact.Edges, edge => Assert.Null(edge.Evidence));
        Assert.Contains(explained.Edges, edge => edge.Evidence?.Reason == "Middle calls End.");
    }

    [Fact]
    public void FindFlowsToSymbol_compact_payload_is_smaller_than_evidence_payload()
    {
        var service = CreateService(CreateGraph());

        var compact = service.FindFlowsToSymbol("End.Run", maxDepth: 4);
        var explained = service.FindFlowsToSymbolWithOptions("End.Run", maxDepth: 4, includeEvidence: true);

        Assert.True(SerializedByteCount(compact) < SerializedByteCount(explained));
        AssertPayloadUnder(compact, 12_000);
    }

    [Fact]
    public void FindFlowsToSymbol_excludes_relations_before_capping()
    {
        var service = CreateService(CreateContainsNoiseGraph());

        var result = service.FindFlowsToSymbolWithOptions("ZDependency", maxDepth: 2, maxResults: 2, excludeRelations: [GraphRelations.Contains]);

        var edge = Assert.Single(result.Edges);
        Assert.Equal(GraphRelations.Injects, edge.Relation);
        Assert.Equal("Service", edge.SourceLabel);
    }

    [Fact]
    public void FindFlowsToSymbol_does_not_claim_endpoint_coverage_limit_when_graph_has_entrypoints()
    {
        var service = CreateService(CreateGraphWithUnrelatedEndpoint());

        var result = service.FindFlowsToSymbol("End.Run", maxDepth: 4);

        Assert.Equal("no_entrypoint_flows", result.Status);
        Assert.Contains(result.Nodes, node => node.Label == "Middle.Run");
        Assert.Null(result.Limitation);
    }

    [Fact]
    public void GetSymbolSummary_returns_compact_member_and_relation_context()
    {
        var service = CreateService(CreateMemberPlanningGraph());

        var result = service.GetSymbolSummary("MintTask", maxResults: 5);

        Assert.Equal("ok", result.Status);
        Assert.Equal("MintTask", result.Node?.Label);
        Assert.Contains(result.ContainedProperties ?? [], node => node.Label == "MintTask.ExecutionStrategy");
        Assert.Equal(1, result.OutgoingRelationCounts?[GraphRelations.Contains]);
        Assert.Contains(result.SuggestedQueries ?? [], query => query.Contains("get_neighbors", StringComparison.Ordinal));
    }

    [Fact]
    public void GetSymbolSummary_counts_implemented_by_as_important_relation()
    {
        var service = CreateService(CreateImplementedByGraph());

        var result = service.GetSymbolSummary("IPlugin", maxResults: 5);

        Assert.Equal("ok", result.Status);
        Assert.Equal(1, result.ImportantRelationCounts?[GraphRelations.ImplementedBy]);
    }

    [Fact]
    public void PlanFeature_ranks_existing_extension_points_for_absent_new_concept()
    {
        var service = CreateService(CreateMemberPlanningGraph());

        var result = service.PlanFeature(
            "add Flashbot execution mode",
            seedSymbols: ["ModuleExecutionStrategy"],
            terms: ["relay"],
            maxResults: 3);

        Assert.Equal("ok", result.Status);
        Assert.Equal("found", Assert.Single(result.Seeds).Status);
        Assert.Equal("ModuleExecutionStrategy", result.EditPoints[0].Node.Label);
        Assert.Equal(GraphNodeKinds.Enum, result.EditPoints[0].Node.Kind);
        Assert.Contains("flashbot", result.Limitation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not present in the loaded Meridian graph", result.Limitation);
        Assert.Contains(result.EditPoints[0].SuggestedQueries, query => query.Contains("get_symbol_summary", StringComparison.Ordinal));
    }

    [Fact]
    public void GetGraphStatistics_returns_compact_counts_limitations_and_suggestions()
    {
        var service = CreateService(CreateMemberPlanningGraph());

        var result = service.GetGraphStatistics(maxDiagnostics: 2);

        Assert.Equal("ok", result.Status);
        Assert.Contains(MeridianMcpMessages.StaleGraphNote, result.StaleGraphNote);
        Assert.Equal(9, result.Statistics?.Graph.NodeCount);
        Assert.Equal(6, result.Statistics?.NodeKindCounts.Count);
        Assert.Equal(5, result.Statistics?.RelationCounts[GraphRelations.Contains]);
        Assert.Contains(result.Limitations ?? [], limitation => limitation.Contains("loaded Meridian graph", StringComparison.Ordinal));
        Assert.Contains(result.SuggestedQueries ?? [], query => query.Contains("get_agent_summary", StringComparison.Ordinal));
    }

    [Fact]
    public void GetAgentSummary_returns_ranked_orientation_without_edge_evidence()
    {
        var service = CreateService(CreateMemberPlanningGraph());

        var result = service.GetAgentSummary("compact", maxItemsPerSection: 2);

        Assert.Equal("ok", result.Status);
        Assert.NotNull(result.Statistics);
        Assert.NotEmpty(result.CentralNodes ?? []);
        Assert.NotEmpty(result.ExtensionPoints ?? []);
        Assert.True(result.Truncated);
        Assert.Contains("capped", result.TruncationNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.ExtensionPoints ?? [], point => point.Node.Label == "ModuleExecutionStrategy");
        Assert.Contains(result.Limitations ?? [], limitation => limitation.Contains("not proof of absence", StringComparison.Ordinal));
        Assert.Contains(result.SuggestedQueries ?? [], query => query.Contains("get_symbol_summary", StringComparison.Ordinal));
    }

    [Fact]
    public void Agent_and_symbol_summary_payloads_stay_bounded_by_caps()
    {
        var service = CreateService(CreateMemberPlanningGraph());

        var agentSummary = service.GetAgentSummary("compact", maxItemsPerSection: 2);
        var symbolSummary = service.GetSymbolSummary("MintTask", maxResults: 3);

        Assert.Equal("ok", agentSummary.Status);
        Assert.Equal("ok", symbolSummary.Status);
        Assert.NotNull(agentSummary.CentralNodes);
        Assert.NotNull(agentSummary.ExtensionPoints);
        Assert.NotNull(symbolSummary.Node);
        Assert.NotNull(symbolSummary.ContainedProperties);
        Assert.True((agentSummary.CentralNodes?.Count ?? 0) <= 2);
        Assert.True((agentSummary.ExtensionPoints?.Count ?? 0) <= 2);
        Assert.True((symbolSummary.ContainedProperties?.Count ?? 0) <= 3);
        AssertPayloadUnder(agentSummary, 35_000);
        AssertPayloadUnder(symbolSummary, 20_000);
    }

    [Theory]
    [InlineData("huge")]
    [InlineData("2")]
    [InlineData("999")]
    public void GetAgentSummary_rejects_unknown_budget(string budget)
    {
        var service = CreateService(CreateGraph());

        var result = service.GetAgentSummary(budget);

        Assert.Equal("invalid_input", result.Status);
        Assert.Contains("compact, standard, or detailed", result.Message);
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

    private static void AssertPayloadUnder<T>(T payload, int maxUtf8Bytes)
    {
        var byteCount = SerializedByteCount(payload);
        Assert.True(byteCount < maxUtf8Bytes, $"Expected payload under {maxUtf8Bytes} bytes, actual {byteCount} bytes.");
    }

    private static int SerializedByteCount<T>(T payload)
    {
        return Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(payload, McpPayloadJsonOptions));
    }

    private static readonly JsonSerializerOptions McpPayloadJsonOptions = CreateMcpPayloadJsonOptions();

    private static JsonSerializerOptions CreateMcpPayloadJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static async Task<MeridianGraphToolService> CreateReloadableService(string graphPath, MeridianMcpServerOptions? options = null)
    {
        var store = await McpGraphStore.CreateAsync(options ?? new MeridianMcpServerOptions { GraphPath = graphPath });
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

    private static GraphDocument CreateDiagnosticHeavyGraph()
    {
        return new GraphDocument
        {
            Diagnostics =
            [
                new GraphDiagnostic { Id = "TEST_ONE", Severity = "warning", Message = "First diagnostic." },
                new GraphDiagnostic { Id = "TEST_TWO", Severity = "warning", Message = "Second diagnostic." }
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

    private static GraphDocument CreateImplementedByGraph()
    {
        var builder = new GraphBuilder();
        AddNode(builder, "type:Sample:Sample.IPlugin", "IPlugin", "Sample.IPlugin", GraphNodeKinds.Type, new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["type_kind"] = "interface"
        });
        AddNode(builder, "type:Sample:Sample.EmailPlugin", "EmailPlugin", "Sample.EmailPlugin", GraphNodeKinds.Type);
        AddEdge(builder, "type:Sample:Sample.IPlugin", "type:Sample:Sample.EmailPlugin", GraphRelations.ImplementedBy, 1, "EmailPlugin implements IPlugin.");
        return builder.Build(".");
    }

    private static GraphDocument CreateMemberPlanningGraph()
    {
        var builder = new GraphBuilder();
        AddNode(builder, "enum:Sample:Sample.ModuleExecutionStrategy", "ModuleExecutionStrategy", "Sample.ModuleExecutionStrategy", GraphNodeKinds.Enum, new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["type_kind"] = "enum"
        });
        AddNode(builder, "enum_member:Sample:Sample.ModuleExecutionStrategy.RainbowTable", "ModuleExecutionStrategy.RainbowTable", "Sample.ModuleExecutionStrategy.RainbowTable", GraphNodeKinds.EnumMember);
        AddNode(builder, "enum_member:Sample:Sample.ModuleExecutionStrategy.RuntimeSigning", "ModuleExecutionStrategy.RuntimeSigning", "Sample.ModuleExecutionStrategy.RuntimeSigning", GraphNodeKinds.EnumMember);
        AddNode(builder, "type:Sample:Sample.IMintModule", "IMintModule", "Sample.IMintModule", GraphNodeKinds.Type, new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["type_kind"] = "interface"
        });
        AddNode(builder, "type:Sample:Sample.MintTask", "MintTask", "Sample.MintTask", GraphNodeKinds.Type);
        AddNode(builder, "property:Sample:Sample.MintTask.ExecutionStrategy", "MintTask.ExecutionStrategy", "Sample.MintTask.ExecutionStrategy", GraphNodeKinds.Property);
        AddNode(builder, "type:Sample:Sample.TaskExecutionOrchestrator", "TaskExecutionOrchestrator", "Sample.TaskExecutionOrchestrator", GraphNodeKinds.Type);
        AddNode(builder, "field:Sample:Sample.TaskExecutionOrchestrator._registry", "TaskExecutionOrchestrator._registry", "Sample.TaskExecutionOrchestrator._registry", GraphNodeKinds.Field);
        AddNode(builder, "method:Sample:Sample.TaskExecutionOrchestrator.Execute()", "TaskExecutionOrchestrator.Execute", "Sample.TaskExecutionOrchestrator.Execute()");
        AddEdge(builder, "enum:Sample:Sample.ModuleExecutionStrategy", "enum_member:Sample:Sample.ModuleExecutionStrategy.RainbowTable", GraphRelations.Contains, 1, "Enum contains member.");
        AddEdge(builder, "enum:Sample:Sample.ModuleExecutionStrategy", "enum_member:Sample:Sample.ModuleExecutionStrategy.RuntimeSigning", GraphRelations.Contains, 2, "Enum contains member.");
        AddEdge(builder, "type:Sample:Sample.MintTask", "property:Sample:Sample.MintTask.ExecutionStrategy", GraphRelations.Contains, 3, "Type contains property.");
        AddEdge(builder, "type:Sample:Sample.TaskExecutionOrchestrator", "field:Sample:Sample.TaskExecutionOrchestrator._registry", GraphRelations.Contains, 4, "Type contains field.");
        AddEdge(builder, "type:Sample:Sample.TaskExecutionOrchestrator", "method:Sample:Sample.TaskExecutionOrchestrator.Execute()", GraphRelations.Contains, 5, "Type contains method.");
        AddEdge(builder, "method:Sample:Sample.TaskExecutionOrchestrator.Execute()", "property:Sample:Sample.MintTask.ExecutionStrategy", GraphRelations.Reads, 6, "Method reads strategy.");
        AddEdge(builder, "method:Sample:Sample.TaskExecutionOrchestrator.Execute()", "enum_member:Sample:Sample.ModuleExecutionStrategy.RuntimeSigning", GraphRelations.Uses, 7, "Method uses enum member.");
        AddEdge(builder, "type:Sample:Sample.IMintModule", "type:Sample:Sample.TaskExecutionOrchestrator", GraphRelations.RegisteredAs, 8, "DI registration.");
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

    private static GraphDocument CreateContainsNoiseGraph()
    {
        var builder = new GraphBuilder();
        AddNode(builder, "type:Sample:Service", "Service", "Sample.Service", GraphNodeKinds.Type);
        AddNode(builder, "type:Sample:ZDependency", "ZDependency", "Sample.ZDependency", GraphNodeKinds.Type);
        for (var i = 0; i < 6; i++)
        {
            var methodId = $"method:Sample:Service.Method{i}()";
            AddNode(builder, methodId, $"Service.Method{i}", $"Sample.Service.Method{i}()");
            AddEdge(builder, "type:Sample:Service", methodId, GraphRelations.Contains, i + 1, $"Service contains method {i}.");
        }

        AddEdge(builder, "type:Sample:Service", "type:Sample:ZDependency", GraphRelations.Injects, 10, "Service injects dependency.");
        return builder.Build(".");
    }

    private static GraphDocument CreateExcludedTraversalGraph()
    {
        var builder = new GraphBuilder();
        AddNode(builder, "type:Sample:Root", "Root", "Sample.Root", GraphNodeKinds.Type);
        AddNode(builder, "method:Sample:Root.Child()", "Root.Child", "Sample.Root.Child()");
        AddNode(builder, "type:Sample:Leaf", "Leaf", "Sample.Leaf", GraphNodeKinds.Type);
        AddEdge(builder, "type:Sample:Root", "method:Sample:Root.Child()", GraphRelations.Contains, 1, "Root contains child.");
        AddEdge(builder, "method:Sample:Root.Child()", "type:Sample:Leaf", GraphRelations.Calls, 2, "Child reaches leaf.");
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

    private static void AddNode(
        GraphBuilder builder,
        string id,
        string label,
        string symbol,
        string kind = GraphNodeKinds.Method,
        SortedDictionary<string, string>? metadata = null)
    {
        builder.AddNode(new GraphNode
        {
            Id = id,
            Label = label,
            Kind = kind,
            Symbol = symbol,
            Metadata = metadata ?? new SortedDictionary<string, string>(StringComparer.Ordinal)
        });
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

using System.Text.Json;
using Meridian.Abstractions;
using Meridian.Mcp;
using Meridian.Mcp.Responses;

namespace Meridian.Benchmarks;

internal static class McpPayloadReport
{
    public static async Task WriteAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        var service = McpPayloadBenchmarks.CreateService(GraphFixtureFactory.CreateBenchmarkPlanningGraph());
        var noiseService = McpPayloadBenchmarks.CreateService(GraphFixtureFactory.CreateBenchmarkContainsNoiseGraph());
        var rows = new List<PayloadReportRow>
        {
            CreateRow("get_graph_statistics", "default", service.GetGraphStatistics(maxDiagnostics: 3)),
            CreateRow("get_agent_summary", "compact,maxItemsPerSection=3", service.GetAgentSummary("compact", maxItemsPerSection: 3)),
            CreateRow("get_agent_summary", "standard,maxItemsPerSection=5", service.GetAgentSummary("standard", maxItemsPerSection: 5)),
            CreateRow("get_symbol_summary", "Component0,maxResults=5", service.GetSymbolSummary("Component0", maxResults: 5)),
            CreateRow("query_graph", "methods,calls,maxResults=10", service.QueryGraphWithOptions(nodeKind: GraphNodeKinds.Method, relation: GraphRelations.Calls, maxResults: 10)),
            CreateRow("query_graph", "methods,calls,maxResults=10,includeEvidence=true", service.QueryGraphWithOptions(nodeKind: GraphNodeKinds.Method, relation: GraphRelations.Calls, includeEvidence: true, maxResults: 10)),
            CreateRow("get_neighbors", "Component0.Operation0,outgoing,depth=2,maxResults=10", service.GetNeighbors("Component0.Operation0", GraphDirection.Outgoing, depth: 2, maxResults: 10)),
            CreateRow("get_neighbors", "Service,outgoing,exclude contains", noiseService.GetNeighborsWithOptions("Service", GraphDirection.Outgoing, depth: 1, maxResults: 10, excludeRelations: [GraphRelations.Contains])),
            CreateRow("shortest_path", "Component0.Operation0 -> Component0.Operation7", service.ShortestPath("Component0.Operation0", "Component0.Operation7")),
            CreateRow("explain_path", "Component0.Operation0 -> Component0.Operation7,includeEvidence=true", service.ExplainPath("Component0.Operation0", "Component0.Operation7", includeEvidence: true)),
            CreateRow("find_flows_to_symbol", "Component0.Operation7,maxDepth=8,maxResults=10", service.FindFlowsToSymbol("Component0.Operation7", maxDepth: 8, maxResults: 10)),
            CreateRow("find_flows_to_symbol", "Component0.Operation7,maxDepth=8,maxResults=10,includeEvidence=true", service.FindFlowsToSymbolWithOptions("Component0.Operation7", maxDepth: 8, maxResults: 10, includeEvidence: true))
        };

        var report = new PayloadReport(
            GeneratedUtc: DateTimeOffset.UtcNow,
            GraphNodeCount: service.GetGraphStatistics().Statistics?.Graph.NodeCount ?? 0,
            GraphEdgeCount: service.GetGraphStatistics().Statistics?.Graph.EdgeCount ?? 0,
            Rows: rows);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }), cancellationToken);
    }

    private static PayloadReportRow CreateRow<T>(string tool, string options, T payload)
    {
        return new PayloadReportRow(
            Tool: tool,
            Options: options,
            Status: GetStatus(payload),
            NodeCount: GetNodeCount(payload),
            EdgeCount: GetEdgeCount(payload),
            Truncated: GetTruncated(payload),
            Utf8Bytes: McpPayloadBenchmarks.SerializedByteCount(payload));
    }

    private static string? GetStatus<T>(T payload)
    {
        return payload switch
        {
            GraphStatisticsResponse response => response.Status,
            AgentSummaryResponse response => response.Status,
            SymbolSummaryResponse response => response.Status,
            GraphSearchResponse response => response.Status,
            PathResponse response => response.Status,
            _ => null
        };
    }

    private static int GetNodeCount<T>(T payload)
    {
        return payload switch
        {
            GraphStatisticsResponse response => response.Statistics?.Graph.NodeCount ?? 0,
            AgentSummaryResponse response => CountAgentSummaryNodes(response),
            SymbolSummaryResponse response => response.Node is null ? 0 : 1,
            GraphSearchResponse response => response.Nodes.Count,
            PathResponse response => (response.Path?.Segments.Count + 1) ?? 0,
            _ => 0
        };
    }

    private static int GetEdgeCount<T>(T payload)
    {
        return payload switch
        {
            GraphStatisticsResponse response => response.Statistics?.Graph.EdgeCount ?? 0,
            AgentSummaryResponse response => response.Clusters?.Sum(cluster => cluster.EdgeCount) ?? 0,
            GraphSearchResponse response => response.Edges.Count,
            PathResponse response => response.Path?.EdgeCount ?? 0,
            _ => 0
        };
    }

    private static int CountAgentSummaryNodes(AgentSummaryResponse response)
    {
        return response.CentralNodes?.Select(summary => summary.Node.Id)
            .Concat(response.ExtensionPoints?.Select(summary => summary.Node.Id) ?? [])
            .Concat(response.Clusters?.SelectMany(cluster => cluster.RepresentativeNodes.Select(node => node.Id)) ?? [])
            .Distinct(StringComparer.Ordinal)
            .Count() ?? 0;
    }

    private static bool GetTruncated<T>(T payload)
    {
        return payload switch
        {
            GraphStatisticsResponse response => response.Truncated,
            AgentSummaryResponse response => response.Truncated,
            SymbolSummaryResponse response => response.Truncated,
            GraphSearchResponse response => response.Truncated,
            PathResponse response => response.Truncated,
            _ => false
        };
    }

    private sealed record PayloadReport(
        DateTimeOffset GeneratedUtc,
        int GraphNodeCount,
        int GraphEdgeCount,
        IReadOnlyList<PayloadReportRow> Rows);

    private sealed record PayloadReportRow(
        string Tool,
        string Options,
        string? Status,
        int NodeCount,
        int EdgeCount,
        bool Truncated,
        int Utf8Bytes);
}

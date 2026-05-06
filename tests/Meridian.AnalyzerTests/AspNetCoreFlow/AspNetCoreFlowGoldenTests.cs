using Meridian.Abstractions;
using Meridian.Exporters.Json;
using Meridian.Roslyn;

namespace Meridian.AnalyzerTests.AspNetCoreFlow;

public sealed class AspNetCoreFlowGoldenTests
{
    [Fact]
    public async Task AspNetCore_flow_analyzer_matches_golden_graph()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "samples", "Sample.AspNetCoreFlow", "Sample.AspNetCoreFlow.csproj");
        var expectedPath = Path.Combine(root, "tests", "Meridian.AnalyzerTests", "AspNetCoreFlow", "Expected", "Sample.AspNetCoreFlow.graph.json");

        var analyzer = new RoslynFlowAnalyzer();
        var graph = await analyzer.AnalyzeAsync(projectPath);
        var actualJson = JsonGraphExporter.Serialize(graph);
        var expectedJson = await File.ReadAllTextAsync(expectedPath);

        Assert.Equal(Normalize(expectedJson), Normalize(actualJson));
        Assert.Contains(graph.Nodes, node => node.Kind == GraphNodeKinds.Endpoint && node.Label == "POST /orders");
        Assert.Contains(graph.Nodes, node => node.Kind == GraphNodeKinds.Endpoint && node.Label == "GET /api/orders/{orderId}");
        Assert.Contains(graph.Nodes, node => node.Kind == GraphNodeKinds.Endpoint && node.Label == "POST /fast/orders");
        Assert.Contains(graph.Nodes, node => node.Kind == GraphNodeKinds.Endpoint && node.Label == "POST /api/minimalapi/orders");
        Assert.Contains(graph.Nodes, node => node.Kind == GraphNodeKinds.Endpoint && node.Label == "GET /health");
        Assert.Contains(graph.Nodes, node => node.Kind == GraphNodeKinds.Endpoint && node.Label == "GET /regex/{id:regex(^\\d+$)}");
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == GraphNodeKinds.Endpoint && node.Label.Contains("/fake", StringComparison.Ordinal));
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == GraphNodeKinds.Endpoint && node.Label.Contains("/Orders/health", StringComparison.Ordinal));
        Assert.Contains(graph.Edges, edge => edge.Relation == GraphRelations.Sends);
        Assert.Contains(graph.Edges, edge => edge.Relation == GraphRelations.HandledBy);
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Id.StartsWith("MERIDIAN_ASPNETCORE_ENDPOINT", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find Meridian repository root.");
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n").TrimEnd();
    }
}

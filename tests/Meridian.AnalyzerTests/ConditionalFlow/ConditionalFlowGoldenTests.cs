using Meridian.Abstractions;
using Meridian.Exporters.Json;
using Meridian.Roslyn;

namespace Meridian.AnalyzerTests.ConditionalFlow;

public sealed class ConditionalFlowGoldenTests
{
    [Fact]
    public async Task Conditional_flow_analyzer_matches_golden_graph()
    {
        var root = FindRepositoryRoot();
        var expectedPath = Path.Combine(root, "tests", "Meridian.AnalyzerTests", "ConditionalFlow", "Expected", "Sample.ConditionalFlow.graph.json");

        var graph = await AnalyzeConditionalFlowAsync(root);
        var actualJson = JsonGraphExporter.Serialize(graph);
        var expectedJson = await File.ReadAllTextAsync(expectedPath);

        Assert.Equal(Normalize(expectedJson), Normalize(actualJson));
    }

    [Fact]
    public async Task Conditional_flow_emits_branch_and_switch_edges()
    {
        var graph = await AnalyzeConditionalFlowAsync();

        Assert.Contains(graph.Edges, edge => edge.Relation == GraphRelations.BranchesOn && edge.Target.Contains("RoutingMode.Fast", StringComparison.Ordinal));
        Assert.Contains(graph.Edges, edge => edge.Relation == GraphRelations.BranchesOn && edge.Target.Contains("CanaryRegion", StringComparison.Ordinal));
        Assert.Contains(graph.Edges, edge => edge.Relation == GraphRelations.SwitchesOn && edge.Target.Contains("RoutingMode.Offline", StringComparison.Ordinal));
    }

    private static Task<GraphDocument> AnalyzeConditionalFlowAsync(string? root = null)
    {
        root ??= FindRepositoryRoot();
        var projectPath = Path.Combine(root, "samples", "Sample.ConditionalFlow", "Sample.ConditionalFlow.csproj");
        var analyzer = new RoslynFlowAnalyzer();
        return analyzer.AnalyzeAsync(projectPath);
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

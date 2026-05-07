using Meridian.Abstractions;
using Meridian.Exporters.Json;
using Meridian.Roslyn;

namespace Meridian.AnalyzerTests.MvvmFlow;

public sealed class MvvmFlowGoldenTests
{
    [Fact]
    public async Task Mvvm_flow_analyzer_matches_golden_graph()
    {
        var root = FindRepositoryRoot();
        var expectedPath = Path.Combine(root, "tests", "Meridian.AnalyzerTests", "MvvmFlow", "Expected", "Sample.MvvmFlow.graph.json");

        var graph = await AnalyzeMvvmFlowAsync(root);
        var actualJson = JsonGraphExporter.Serialize(graph);
        var expectedJson = await File.ReadAllTextAsync(expectedPath);

        Assert.Equal(Normalize(expectedJson), Normalize(actualJson));
    }

    [Fact]
    public async Task Mvvm_flow_emits_generated_command_and_property_edges()
    {
        var graph = await AnalyzeMvvmFlowAsync();

        Assert.Contains(graph.Nodes, node => node.Kind == GraphNodeKinds.MvvmCommand && node.Label == "RecordingViewModel.ToggleRecordingCommand");
        Assert.Contains(graph.Nodes, node => node.Kind == GraphNodeKinds.MvvmCommand && node.Label == "RecordingViewModel.ExportCommand");
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == GraphNodeKinds.MvvmCommand && node.Label == "RecordingViewModel.OnExportCommand");
        Assert.Contains(graph.Nodes, node => node.Kind == GraphNodeKinds.Property && node.Label == "RecordingViewModel.IsRecording");
        Assert.Contains(graph.Edges, edge => edge.Relation == GraphRelations.GeneratedFrom && edge.Source.Contains("ToggleRecordingCommand", StringComparison.Ordinal));
        Assert.Contains(graph.Edges, edge => edge.Relation == GraphRelations.GeneratedFrom && edge.Source.Contains("ExportCommand", StringComparison.Ordinal));
    }

    private static Task<GraphDocument> AnalyzeMvvmFlowAsync(string? root = null)
    {
        root ??= FindRepositoryRoot();
        var projectPath = Path.Combine(root, "samples", "Sample.MvvmFlow", "Sample.MvvmFlow.csproj");
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

using Meridian.Exporters.Json;
using Meridian.Roslyn;

namespace Meridian.AnalyzerTests.BasicCalls;

public sealed class BasicCallsGoldenTests
{
    [Fact]
    public async Task Direct_call_analyzer_matches_golden_graph()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "samples", "Sample.BasicCalls", "Sample.BasicCalls.csproj");
        var expectedPath = Path.Combine(root, "tests", "Meridian.AnalyzerTests", "BasicCalls", "Expected", "BasicCalls.graph.json");

        var analyzer = new RoslynFlowAnalyzer();
        var graph = await analyzer.AnalyzeAsync(projectPath);
        var actualJson = JsonGraphExporter.Serialize(graph);
        var expectedJson = await File.ReadAllTextAsync(expectedPath);

        Assert.Equal(Normalize(expectedJson), Normalize(actualJson));
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

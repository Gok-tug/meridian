using Meridian.Exporters.Json;
using Meridian.Roslyn;

namespace Meridian.AnalyzerTests.DynamicWiring;

public sealed class DynamicWiringGoldenTests
{
    [Fact]
    public async Task Dynamic_wiring_analyzer_matches_golden_graph()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "samples", "Sample.DynamicWiring", "Sample.DynamicWiring.csproj");
        var expectedPath = Path.Combine(root, "tests", "Meridian.AnalyzerTests", "DynamicWiring", "Expected", "Sample.DynamicWiring.graph.json");

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

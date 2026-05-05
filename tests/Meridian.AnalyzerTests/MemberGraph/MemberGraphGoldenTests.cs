using Meridian.Abstractions;
using Meridian.Exporters.Json;
using Meridian.Roslyn;

namespace Meridian.AnalyzerTests.MemberGraph;

public sealed class MemberGraphGoldenTests
{
    [Fact]
    public async Task Member_graph_analyzer_matches_golden_graph()
    {
        var root = FindRepositoryRoot();
        var expectedPath = Path.Combine(root, "tests", "Meridian.AnalyzerTests", "MemberGraph", "Expected", "Sample.MemberGraph.graph.json");

        var graph = await AnalyzeMemberGraphAsync(root);
        var actualJson = JsonGraphExporter.Serialize(graph);
        var expectedJson = await File.ReadAllTextAsync(expectedPath);

        Assert.Equal(Normalize(expectedJson), Normalize(actualJson));
    }

    [Fact]
    public async Task Member_graph_does_not_emit_enum_use_for_implicit_var_type()
    {
        var graph = await AnalyzeMemberGraphAsync();

        Assert.DoesNotContain(graph.Edges, edge =>
            edge.Relation == GraphRelations.Uses &&
            edge.Target == "enum:Sample.MemberGraph:Sample.MemberGraph.ExecutionMode" &&
            edge.Evidence?.Reason?.Contains("member reference 'var'", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Enum_member_metadata_omits_redundant_const_and_static_flags()
    {
        var graph = await AnalyzeMemberGraphAsync();
        var enumMembers = graph.Nodes
            .Where(node => node.Kind == GraphNodeKinds.EnumMember)
            .ToArray();

        Assert.NotEmpty(enumMembers);
        Assert.All(enumMembers, node =>
        {
            Assert.False(node.Metadata.ContainsKey("is_const"));
            Assert.False(node.Metadata.ContainsKey("is_static"));
        });
    }

    private static Task<GraphDocument> AnalyzeMemberGraphAsync(string? root = null)
    {
        root ??= FindRepositoryRoot();
        var projectPath = Path.Combine(root, "samples", "Sample.MemberGraph", "Sample.MemberGraph.csproj");
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

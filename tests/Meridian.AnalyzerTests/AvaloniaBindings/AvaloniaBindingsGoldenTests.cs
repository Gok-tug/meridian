using Meridian.Abstractions;
using Meridian.Exporters.Json;
using Meridian.Roslyn;

namespace Meridian.AnalyzerTests.AvaloniaBindings;

public sealed class AvaloniaBindingsGoldenTests
{
    [Fact]
    public async Task Avalonia_bindings_analyzer_matches_golden_graph()
    {
        var root = FindRepositoryRoot();
        var expectedPath = Path.Combine(root, "tests", "Meridian.AnalyzerTests", "AvaloniaBindings", "Expected", "Sample.AvaloniaBindings.graph.json");

        var graph = await AnalyzeAvaloniaBindingsAsync(root);
        var actualJson = JsonGraphExporter.Serialize(graph);
        var expectedJson = await File.ReadAllTextAsync(expectedPath);

        Assert.Equal(Normalize(expectedJson), Normalize(actualJson));
    }

    [Fact]
    public async Task Avalonia_bindings_emit_truncation_diagnostic_when_file_cap_is_exceeded()
    {
        var projectDirectory = Path.Combine(Path.GetTempPath(), "Meridian.Axaml.Truncation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDirectory);
        try
        {
            var projectPath = Path.Combine(projectDirectory, "TruncationSample.csproj");
            await File.WriteAllTextAsync(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""");
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Placeholder.cs"), "namespace TruncationSample; public sealed class Placeholder { }");
            var bindingElements = string.Join(Environment.NewLine, Enumerable.Range(0, 30).Select(index => $"    <TextBlock Text=\"{{Binding $parent[Window].Missing{index}}}\" />"));
            await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Truncated.axaml"), """
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <StackPanel>
""" + Environment.NewLine + bindingElements + Environment.NewLine + """
  </StackPanel>
</UserControl>
""");

            var graph = await new RoslynFlowAnalyzer().AnalyzeAsync(projectPath, new RoslynFlowAnalysisOptions { EmitMsBuildTrustBoundaryDiagnostic = false });
            var axamlDiagnostics = graph.Diagnostics
                .Where(diagnostic => diagnostic.SourceFile?.EndsWith("Truncated.axaml", StringComparison.Ordinal) == true)
                .ToArray();
            var truncation = Assert.Single(axamlDiagnostics, diagnostic => diagnostic.Id == "MERIDIAN_AXAML_DIAGNOSTICS_TRUNCATED");

            Assert.Equal(25, axamlDiagnostics.Count(diagnostic => diagnostic.Id.StartsWith("MERIDIAN_AXAML_", StringComparison.Ordinal)));
            Assert.Equal(24, axamlDiagnostics.Count(diagnostic => diagnostic.Id == "MERIDIAN_AXAML_BINDING_UNSUPPORTED"));
            Assert.Contains("6 additional unique AXAML diagnostic", truncation.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(projectDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Avalonia_bindings_resolve_typed_scopes_to_members_commands_and_view_models()
    {
        var graph = await AnalyzeAvaloniaBindingsAsync();

        Assert.Contains(graph.Edges, edge => IsBindingTo(edge, "Title", GraphNodeKinds.Property));
        Assert.Contains(graph.Edges, edge => IsBindingTo(edge, "Header", GraphNodeKinds.Property));
        Assert.Contains(graph.Edges, edge => IsBindingTo(edge, "StatusMessage", GraphNodeKinds.Property));
        Assert.Contains(graph.Edges, edge => IsBindingTo(edge, "SearchText", GraphNodeKinds.Property) && edge.Metadata["has_converter"] == "true");
        Assert.Contains(graph.Edges, edge => IsBindingTo(edge, "SaveCommand", GraphNodeKinds.MvvmCommand));
        Assert.Contains(graph.Edges, edge => IsBindingTo(edge, "CancelCommand", GraphNodeKinds.MvvmCommand));
        Assert.Contains(graph.Edges, edge => IsBindingTo(edge, "Name", GraphNodeKinds.Property) && edge.Metadata["binding_kind"] == "compiled_binding");
        Assert.Contains(graph.Edges, edge => IsBindingTo(edge, "RemoveExpansionCommand", GraphNodeKinds.MvvmCommand) && edge.Metadata["binding_scope"] == "typed_datacontext_cast");
        Assert.Contains(graph.Edges, edge => edge.Relation == GraphRelations.BindsTo && edge.Source.Contains("TextExpansionView", StringComparison.Ordinal) && edge.Target.Contains("MainWindowViewModel", StringComparison.Ordinal));
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Id == "MERIDIAN_AXAML_BINDING_UNSUPPORTED");
        Assert.DoesNotContain(graph.Edges, edge => edge.Relation == GraphRelations.BindsTo && edge.Metadata.TryGetValue("binding_path", out var path) && path.Contains("$parent", StringComparison.Ordinal));
    }

    private static bool IsBindingTo(GraphEdge edge, string path, string targetKind)
    {
        return edge.Relation == GraphRelations.BindsTo &&
            edge.Metadata.TryGetValue("binding_path", out var bindingPath) &&
            bindingPath.Equals(path, StringComparison.Ordinal) &&
            edge.Target.StartsWith(targetKind + ":", StringComparison.Ordinal);
    }

    private static Task<GraphDocument> AnalyzeAvaloniaBindingsAsync(string? root = null)
    {
        root ??= FindRepositoryRoot();
        var projectPath = Path.Combine(root, "samples", "Sample.AvaloniaBindings", "Sample.AvaloniaBindings.csproj");
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

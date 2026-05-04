using Meridian.Abstractions;
using Meridian.Core;
using Meridian.Exporters.Json;

namespace Meridian.Core.Tests;

public sealed class GraphBuilderTests
{
    [Fact]
    public void Build_returns_nodes_and_edges_in_deterministic_order()
    {
        var builder = new GraphBuilder();
        builder.AddNode(new GraphNode { Id = "method:B", Label = "B", Kind = GraphNodeKinds.Method });
        builder.AddNode(new GraphNode { Id = "method:A", Label = "A", Kind = GraphNodeKinds.Method });
        builder.AddEdge(new GraphEdge
        {
            Source = "method:B",
            Target = "method:A",
            Relation = GraphRelations.Calls,
            Confidence = ConfidenceLevels.Extracted
        });

        var graph = builder.Build("sample-root");
        var json = JsonGraphExporter.Serialize(graph);

        Assert.Equal(["method:A", "method:B"], graph.Nodes.Select(node => node.Id).ToArray());
        Assert.Contains("\"schema_version\": \"0.1\"", json);
        Assert.Contains("\"confidence\": \"EXTRACTED\"", json);
    }
}

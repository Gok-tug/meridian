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

    [Fact]
    public void Build_orders_edges_deterministically_by_edge_key()
    {
        var builder = new GraphBuilder();
        builder.AddEdge(Edge("method:B", "method:A", GraphRelations.Calls, 2, "B calls A"));
        builder.AddEdge(Edge("method:A", "method:B", GraphRelations.Uses, 2, "A uses B"));
        builder.AddEdge(Edge("method:A", "method:B", GraphRelations.Calls, 2, "second call"));
        builder.AddEdge(Edge("method:A", "method:B", GraphRelations.Calls, 1, "first call"));

        var graph = builder.Build();

        Assert.Equal(
            [
                "method:A|method:B|calls|Sample.cs|1|first call",
                "method:A|method:B|calls|Sample.cs|2|second call",
                "method:A|method:B|uses|Sample.cs|2|A uses B",
                "method:B|method:A|calls|Sample.cs|2|B calls A"
            ],
            graph.Edges.Select(EdgeSignature).ToArray());
    }

    [Fact]
    public void Build_deduplicates_identical_edges_but_preserves_distinct_evidence()
    {
        var builder = new GraphBuilder();
        builder.AddEdge(Edge("method:A", "method:B", GraphRelations.Calls, 1, "same evidence"));
        builder.AddEdge(Edge("method:A", "method:B", GraphRelations.Calls, 1, "same evidence"));
        builder.AddEdge(Edge("method:A", "method:B", GraphRelations.Calls, 2, "different evidence"));

        var graph = builder.Build();

        Assert.Equal(
            [
                "method:A|method:B|calls|Sample.cs|1|same evidence",
                "method:A|method:B|calls|Sample.cs|2|different evidence"
            ],
            graph.Edges.Select(EdgeSignature).ToArray());
    }

    [Fact]
    public void Build_orders_diagnostics_by_id_then_message()
    {
        var builder = new GraphBuilder();
        builder.AddDiagnostic(Diagnostic("MERIDIAN002", "Beta"));
        builder.AddDiagnostic(Diagnostic("MERIDIAN001", "Zeta"));
        builder.AddDiagnostic(Diagnostic("MERIDIAN001", "Alpha"));

        var graph = builder.Build();

        Assert.Equal(
            [
                "MERIDIAN001|Alpha",
                "MERIDIAN001|Zeta",
                "MERIDIAN002|Beta"
            ],
            graph.Diagnostics.Select(diagnostic => $"{diagnostic.Id}|{diagnostic.Message}").ToArray());
    }

    private static GraphEdge Edge(string source, string target, string relation, int line, string reason)
    {
        return new GraphEdge
        {
            Source = source,
            Target = target,
            Relation = relation,
            Confidence = ConfidenceLevels.Extracted,
            Evidence = new GraphEvidence
            {
                File = "Sample.cs",
                Line = line,
                Reason = reason
            }
        };
    }

    private static GraphDiagnostic Diagnostic(string id, string message)
    {
        return new GraphDiagnostic
        {
            Id = id,
            Severity = "warning",
            Message = message
        };
    }

    private static string EdgeSignature(GraphEdge edge)
    {
        return $"{edge.Source}|{edge.Target}|{edge.Relation}|{edge.Evidence?.File}|{edge.Evidence?.Line}|{edge.Evidence?.Reason}";
    }
}

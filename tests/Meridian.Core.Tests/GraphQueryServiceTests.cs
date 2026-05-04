using Meridian.Abstractions;
using Meridian.Core;

namespace Meridian.Core.Tests;

public sealed class GraphQueryServiceTests
{
    [Fact]
    public void Explain_returns_matching_node_with_incoming_and_outgoing_edges()
    {
        var graph = CreateGraph();
        var query = new GraphQueryService(graph);

        var result = query.Explain("Middle.Run");

        Assert.NotNull(result);
        Assert.Equal("Middle.Run", result.Node.Label);
        Assert.Single(result.IncomingEdges);
        Assert.Single(result.OutgoingEdges);
    }

    [Fact]
    public void FindPath_returns_shortest_directed_path()
    {
        var graph = CreateGraph();
        var query = new GraphQueryService(graph);

        var result = query.FindPath("Start.Run", "End.Run");

        Assert.NotNull(result);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal("Start.Run", result.Segments[0].Source.Label);
        Assert.Equal("Middle.Run", result.Segments[0].Target.Label);
        Assert.Equal("End.Run", result.Segments[1].Target.Label);
    }

    private static GraphDocument CreateGraph()
    {
        var builder = new GraphBuilder();
        builder.AddNode(new GraphNode { Id = "method:Sample:Start.Run()", Label = "Start.Run", Kind = GraphNodeKinds.Method, Symbol = "Sample.Start.Run()" });
        builder.AddNode(new GraphNode { Id = "method:Sample:Middle.Run()", Label = "Middle.Run", Kind = GraphNodeKinds.Method, Symbol = "Sample.Middle.Run()" });
        builder.AddNode(new GraphNode { Id = "method:Sample:End.Run()", Label = "End.Run", Kind = GraphNodeKinds.Method, Symbol = "Sample.End.Run()" });
        builder.AddEdge(new GraphEdge
        {
            Source = "method:Sample:Start.Run()",
            Target = "method:Sample:Middle.Run()",
            Relation = GraphRelations.Calls,
            Confidence = ConfidenceLevels.Extracted,
            Evidence = new GraphEvidence { File = "Sample.cs", Line = 10 }
        });
        builder.AddEdge(new GraphEdge
        {
            Source = "method:Sample:Middle.Run()",
            Target = "method:Sample:End.Run()",
            Relation = GraphRelations.Calls,
            Confidence = ConfidenceLevels.Extracted,
            Evidence = new GraphEvidence { File = "Sample.cs", Line = 20 }
        });
        return builder.Build(".");
    }
}

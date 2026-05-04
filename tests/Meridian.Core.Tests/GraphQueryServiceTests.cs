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

    [Fact]
    public void ResolveNode_exact_id_returns_single_match_when_labels_collide()
    {
        var graph = CreateAmbiguousGraph();
        var query = new GraphQueryService(graph);

        var resolution = query.ResolveNode("method:Sample:Second.Run()");

        Assert.Equal(GraphNodeResolutionStatus.Found, resolution.Status);
        Assert.Equal("method:Sample:Second.Run()", resolution.Node?.Id);
        Assert.Single(resolution.Candidates);
    }

    [Fact]
    public void ResolveNode_unique_exact_label_returns_single_match()
    {
        var graph = CreateGraph();
        var query = new GraphQueryService(graph);

        var resolution = query.ResolveNode("Middle.Run");

        Assert.Equal(GraphNodeResolutionStatus.Found, resolution.Status);
        Assert.Equal("method:Sample:Middle.Run()", resolution.Node?.Id);
    }

    [Fact]
    public void ResolveNode_same_score_label_matches_are_ambiguous()
    {
        var graph = CreateAmbiguousGraph();
        var query = new GraphQueryService(graph);

        var resolution = query.ResolveNode("Run");

        Assert.Equal(GraphNodeResolutionStatus.Ambiguous, resolution.Status);
        Assert.Null(resolution.Node);
        Assert.Equal(2, resolution.Candidates.Count);
        Assert.All(resolution.Candidates, candidate => Assert.Equal(90, candidate.Score));
    }

    [Fact]
    public void FindPath_returns_null_when_source_or_target_query_is_ambiguous()
    {
        var graph = CreateAmbiguousGraph();
        var query = new GraphQueryService(graph);

        Assert.Null(query.FindPath("Run", "End.Run"));
        Assert.Null(query.FindPath("Start.Run", "Run"));
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

    private static GraphDocument CreateAmbiguousGraph()
    {
        var builder = new GraphBuilder();
        builder.AddNode(new GraphNode { Id = "method:Sample:Start.Run()", Label = "Start.Run", Kind = GraphNodeKinds.Method, Symbol = "Sample.Start.Run()" });
        builder.AddNode(new GraphNode { Id = "method:Sample:First.Run()", Label = "Run", Kind = GraphNodeKinds.Method, Symbol = "Sample.First.Run()" });
        builder.AddNode(new GraphNode { Id = "method:Sample:Second.Run()", Label = "Run", Kind = GraphNodeKinds.Method, Symbol = "Sample.Second.Run()" });
        builder.AddNode(new GraphNode { Id = "method:Sample:End.Run()", Label = "End.Run", Kind = GraphNodeKinds.Method, Symbol = "Sample.End.Run()" });
        builder.AddEdge(new GraphEdge
        {
            Source = "method:Sample:Start.Run()",
            Target = "method:Sample:First.Run()",
            Relation = GraphRelations.Calls,
            Confidence = ConfidenceLevels.Extracted,
            Evidence = new GraphEvidence { File = "Sample.cs", Line = 10 }
        });
        builder.AddEdge(new GraphEdge
        {
            Source = "method:Sample:First.Run()",
            Target = "method:Sample:End.Run()",
            Relation = GraphRelations.Calls,
            Confidence = ConfidenceLevels.Extracted,
            Evidence = new GraphEvidence { File = "Sample.cs", Line = 20 }
        });
        return builder.Build(".");
    }
}

using Meridian.Abstractions;

namespace Meridian.Core;

public enum GraphSummaryBudget
{
    Compact,
    Standard,
    Detailed
}

public static class GraphSummaryBudgetParser
{
    public static bool TryParse(string? value, out GraphSummaryBudget budget)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            budget = GraphSummaryBudget.Standard;
            return true;
        }

        foreach (var candidate in Enum.GetValues<GraphSummaryBudget>())
        {
            if (value.Equals(candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                budget = candidate;
                return true;
            }
        }

        budget = GraphSummaryBudget.Standard;
        return false;
    }
}

public sealed record GraphSummaryOptions
{
    public GraphSummaryBudget Budget { get; init; } = GraphSummaryBudget.Standard;

    public int? MaxItemsPerSection { get; init; }

    public int MaxDiagnostics { get; init; } = 5;

    public int EffectiveMaxItemsPerSection()
    {
        var value = MaxItemsPerSection ?? Budget switch
        {
            GraphSummaryBudget.Compact => 3,
            GraphSummaryBudget.Detailed => 10,
            _ => 5
        };
        return Math.Clamp(value, 1, 25);
    }
}

public sealed record GraphMetadataSummary(
    string SchemaVersion,
    string Generator,
    string GeneratorVersion,
    string? Root,
    int NodeCount,
    int EdgeCount,
    int DiagnosticCount)
{
    public static GraphMetadataSummary From(GraphDocument graph)
    {
        return new GraphMetadataSummary(
            graph.SchemaVersion,
            graph.Generator,
            graph.GeneratorVersion,
            graph.Root,
            graph.Nodes.Count,
            graph.Edges.Count,
            graph.Diagnostics.Count);
    }
}

public sealed record GraphStatistics(
    GraphMetadataSummary Graph,
    IReadOnlyDictionary<string, int> NodeKindCounts,
    IReadOnlyDictionary<string, int> RelationCounts,
    IReadOnlyDictionary<string, int> ConfidenceCounts,
    IReadOnlyDictionary<string, int> DiagnosticSeverityCounts,
    IReadOnlyList<GraphDiagnosticSummary> TopDiagnostics,
    bool DiagnosticsTruncated);

public sealed record GraphDiagnosticSummary(
    string Id,
    string Severity,
    string Message,
    int Count,
    string? SourceFile,
    string? SourceLocation);

public sealed record AgentSummaryResult(
    GraphStatistics Statistics,
    IReadOnlyList<RankedGraphNodeSummary> CentralNodes,
    IReadOnlyList<RankedGraphNodeSummary> ExtensionPoints,
    IReadOnlyList<GraphClusterSummary> Clusters,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> SuggestedQueries,
    bool Truncated,
    string? TruncationNote);

public sealed record RankedGraphNodeSummary(
    int Rank,
    int Score,
    GraphNodeSummary Node,
    int NonContainmentDegree,
    int RelationDiversity,
    IReadOnlyDictionary<string, int> RelationCounts,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> SuggestedQueries);

public sealed record GraphNodeSummary(
    string Id,
    string Label,
    string Kind,
    string? Symbol,
    string? SourceFile,
    string? SourceLocation)
{
    public static GraphNodeSummary From(GraphNode node)
    {
        return new GraphNodeSummary(
            node.Id,
            node.Label,
            node.Kind,
            node.Symbol,
            node.SourceFile,
            node.SourceLocation);
    }
}

public sealed record GraphClusterSummary(
    int Rank,
    int NodeCount,
    int EdgeCount,
    IReadOnlyDictionary<string, int> TopNodeKinds,
    IReadOnlyDictionary<string, int> TopRelations,
    IReadOnlyList<GraphNodeSummary> RepresentativeNodes,
    string Limitation);

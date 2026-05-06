using Meridian.Abstractions;

namespace Meridian.Core;

public static class GraphStatisticsBuilder
{
    public static GraphStatistics Build(GraphDocument graph, int maxDiagnostics = 5)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var diagnosticLimit = Math.Clamp(maxDiagnostics, 1, 25);
        var diagnostics = graph.Diagnostics
            .GroupBy(diagnostic => (diagnostic.Id, diagnostic.Severity, diagnostic.Message))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Id, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Severity, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Message, StringComparer.Ordinal)
            .ToArray();
        var topDiagnostics = diagnostics
            .Take(diagnosticLimit)
            .Select(group =>
            {
                var first = group
                    .OrderBy(diagnostic => diagnostic.SourceFile, StringComparer.Ordinal)
                    .ThenBy(diagnostic => diagnostic.SourceLocation, StringComparer.Ordinal)
                    .First();
                return new GraphDiagnosticSummary(
                    group.Key.Id,
                    group.Key.Severity,
                    group.Key.Message,
                    group.Count(),
                    first.SourceFile,
                    first.SourceLocation);
            })
            .ToArray();

        return new GraphStatistics(
            GraphMetadataSummary.From(graph),
            CountBy(graph.Nodes.Select(node => node.Kind)),
            CountBy(graph.Edges.Select(edge => edge.Relation)),
            CountBy(graph.Edges.Select(edge => edge.Confidence)),
            CountBy(graph.Diagnostics.Select(diagnostic => diagnostic.Severity)),
            topDiagnostics,
            diagnostics.Length > diagnosticLimit);
    }

    public static IReadOnlyDictionary<string, int> CountRelations(IEnumerable<GraphEdge> edges)
    {
        return CountBy(edges.Select(edge => edge.Relation));
    }

    public static IReadOnlyDictionary<string, int> CountBy(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }
}

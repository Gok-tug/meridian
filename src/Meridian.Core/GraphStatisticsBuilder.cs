using Meridian.Abstractions;

namespace Meridian.Core;

public static class GraphStatisticsBuilder
{
    private const int MaxDiagnosticGroupSamples = 3;

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
        var diagnosticGroups = BuildDiagnosticGroups(graph.Diagnostics, diagnosticLimit);

        return new GraphStatistics(
            GraphMetadataSummary.From(graph),
            CountBy(graph.Nodes.Select(node => node.Kind)),
            CountBy(graph.Edges.Select(edge => edge.Relation)),
            CountBy(graph.Edges.Select(edge => edge.Confidence)),
            CountBy(graph.Diagnostics.Select(diagnostic => diagnostic.Severity)),
            topDiagnostics,
            diagnostics.Length > diagnosticLimit,
            diagnosticGroups.Items,
            diagnosticGroups.Truncated);
    }

    public static IReadOnlyDictionary<string, int> CountRelations(IEnumerable<GraphEdge> edges)
    {
        return CountBy(edges.Select(edge => edge.Relation));
    }

    public static IReadOnlyList<GraphDiagnosticGroupSummary> GroupDiagnostics(IEnumerable<GraphDiagnostic> diagnostics, int maxGroups = 5)
    {
        return BuildDiagnosticGroups(diagnostics, Math.Clamp(maxGroups, 1, 25)).Items;
    }

    public static IReadOnlyDictionary<string, int> CountBy(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private static (IReadOnlyList<GraphDiagnosticGroupSummary> Items, bool Truncated) BuildDiagnosticGroups(IEnumerable<GraphDiagnostic> diagnostics, int limit)
    {
        var groups = diagnostics
            .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic.Id) && !string.IsNullOrWhiteSpace(diagnostic.Severity))
            .GroupBy(diagnostic => (diagnostic.Id, diagnostic.Severity), StringTupleComparer.Instance)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Id, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Severity, StringComparer.Ordinal)
            .ToArray();
        var items = groups
            .Take(limit)
            .Select(group =>
            {
                var messages = group
                    .Select(diagnostic => diagnostic.Message)
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var locations = group
                    .OrderBy(diagnostic => diagnostic.SourceFile, StringComparer.Ordinal)
                    .ThenBy(diagnostic => diagnostic.SourceLocation, StringComparer.Ordinal)
                    .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                    .Select(diagnostic => new GraphDiagnosticSample(diagnostic.Message, diagnostic.SourceFile, diagnostic.SourceLocation))
                    .Distinct()
                    .ToArray();
                var sourceFileCount = group
                    .Select(diagnostic => diagnostic.SourceFile)
                    .Where(sourceFile => !string.IsNullOrWhiteSpace(sourceFile))
                    .Distinct(StringComparer.Ordinal)
                    .Count();

                return new GraphDiagnosticGroupSummary(
                    group.Key.Id,
                    group.Key.Severity,
                    DiagnosticArea(group.Key.Id),
                    group.Count(),
                    messages.Length,
                    sourceFileCount,
                    messages.Take(MaxDiagnosticGroupSamples).ToArray(),
                    locations.Take(MaxDiagnosticGroupSamples).ToArray(),
                    messages.Length > MaxDiagnosticGroupSamples || locations.Length > MaxDiagnosticGroupSamples);
            })
            .ToArray();
        return (items, groups.Length > limit);
    }

    private static string DiagnosticArea(string id)
    {
        const string prefix = "MERIDIAN_";
        if (!id.StartsWith(prefix, StringComparison.Ordinal))
        {
            return "unknown";
        }

        var areaStart = prefix.Length;
        var areaEnd = id.IndexOf('_', areaStart);
        return areaEnd < 0 ? id[areaStart..] : id[areaStart..areaEnd];
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Id, string Severity)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string Id, string Severity) x, (string Id, string Severity) y)
        {
            return string.Equals(x.Id, y.Id, StringComparison.Ordinal) &&
                string.Equals(x.Severity, y.Severity, StringComparison.Ordinal);
        }

        public int GetHashCode((string Id, string Severity) obj)
        {
            return HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Id), StringComparer.Ordinal.GetHashCode(obj.Severity));
        }
    }
}

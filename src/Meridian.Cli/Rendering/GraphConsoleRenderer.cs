using Meridian.Abstractions;
using Meridian.Core;

namespace Meridian.Cli.Rendering;

internal static class GraphConsoleRenderer
{
    public static void PrintExplain(GraphExplainResult result)
    {
        Console.WriteLine(result.Node.Label);
        Console.WriteLine($"Kind: {result.Node.Kind}");
        if (!string.IsNullOrWhiteSpace(result.Node.Symbol))
        {
            Console.WriteLine($"Symbol: {result.Node.Symbol}");
        }

        if (!string.IsNullOrWhiteSpace(result.Node.SourceFile))
        {
            Console.WriteLine($"Source: {FormatLocation(result.Node.SourceFile, result.Node.SourceLocation)}");
        }

        Console.WriteLine();
        PrintEdges("Incoming", result.IncomingEdges, result.Node.Id);
        Console.WriteLine();
        PrintEdges("Outgoing", result.OutgoingEdges, result.Node.Id);
    }

    public static void PrintPath(GraphPathResult result)
    {
        Console.WriteLine($"Path found: {result.Source.Label} -> {result.Target.Label}");
        Console.WriteLine($"Edges: {result.Segments.Count}");
        Console.WriteLine();

        if (result.Segments.Count == 0)
        {
            Console.WriteLine(result.Source.Label);
            return;
        }

        Console.WriteLine(result.Source.Label);
        foreach (var segment in result.Segments)
        {
            Console.WriteLine($"  --{segment.Edge.Relation}--> {segment.Target.Label} [{segment.Edge.Confidence}]");
            if (segment.Edge.Evidence is { } evidence)
            {
                Console.WriteLine($"    evidence: {FormatEvidence(evidence)}");
            }
        }
    }

    public static void PrintNodeCandidates(string query, IReadOnlyList<GraphNodeMatch> candidates, TextWriter writer)
    {
        writer.WriteLine($"Candidates for '{query}':");
        foreach (var match in candidates.Take(20))
        {
            writer.WriteLine($"  {match.Node.Label} ({match.Node.Kind}) score={match.Score}");
            if (!string.IsNullOrWhiteSpace(match.Node.Symbol))
            {
                writer.WriteLine($"    symbol: {match.Node.Symbol}");
            }

            writer.WriteLine($"    id: {match.Node.Id}");
            if (!string.IsNullOrWhiteSpace(match.Node.SourceFile))
            {
                writer.WriteLine($"    source: {FormatLocation(match.Node.SourceFile, match.Node.SourceLocation)}");
            }
        }

        if (candidates.Count > 20)
        {
            writer.WriteLine($"  ... and {candidates.Count - 20} more");
        }
    }

    public static void PrintAgentSummary(AgentSummaryResult summary)
    {
        Console.WriteLine("Graph");
        Console.WriteLine($"  Schema: {summary.Statistics.Graph.SchemaVersion}");
        Console.WriteLine($"  Generator: {summary.Statistics.Graph.Generator} {summary.Statistics.Graph.GeneratorVersion}");
        if (!string.IsNullOrWhiteSpace(summary.Statistics.Graph.Root))
        {
            Console.WriteLine($"  Root: {summary.Statistics.Graph.Root}");
        }

        Console.WriteLine($"  Nodes: {summary.Statistics.Graph.NodeCount}");
        Console.WriteLine($"  Edges: {summary.Statistics.Graph.EdgeCount}");
        Console.WriteLine($"  Diagnostics: {summary.Statistics.Graph.DiagnosticCount}");
        Console.WriteLine();

        PrintCounts("Node kinds", summary.Statistics.NodeKindCounts);
        Console.WriteLine();
        PrintCounts("Relations", summary.Statistics.RelationCounts);
        Console.WriteLine();
        PrintRankedNodes("Central nodes", summary.CentralNodes);
        Console.WriteLine();
        PrintRankedNodes("Likely extension points", summary.ExtensionPoints);
        Console.WriteLine();
        PrintClusters(summary.Clusters);
        Console.WriteLine();
        PrintList("Limitations", summary.Limitations);
        Console.WriteLine();
        PrintList("Suggested MCP queries", summary.SuggestedQueries);
        if (summary.Truncated && !string.IsNullOrWhiteSpace(summary.TruncationNote))
        {
            Console.WriteLine();
            Console.WriteLine(summary.TruncationNote);
        }
    }

    private static void PrintCounts(string title, IReadOnlyDictionary<string, int> counts)
    {
        Console.WriteLine(title);
        if (counts.Count == 0)
        {
            Console.WriteLine("  none");
            return;
        }

        foreach (var pair in counts)
        {
            Console.WriteLine($"  {pair.Key}: {pair.Value}");
        }
    }

    private static void PrintRankedNodes(string title, IReadOnlyList<RankedGraphNodeSummary> nodes)
    {
        Console.WriteLine(title);
        if (nodes.Count == 0)
        {
            Console.WriteLine("  none");
            return;
        }

        foreach (var item in nodes)
        {
            Console.WriteLine($"  {item.Rank}. {item.Node.Label} ({item.Node.Kind}) score={item.Score}");
            if (!string.IsNullOrWhiteSpace(item.Node.Symbol))
            {
                Console.WriteLine($"     symbol: {item.Node.Symbol}");
            }

            Console.WriteLine($"     id: {item.Node.Id}");
            if (!string.IsNullOrWhiteSpace(item.Node.SourceFile))
            {
                Console.WriteLine($"     source: {FormatLocation(item.Node.SourceFile, item.Node.SourceLocation)}");
            }

            Console.WriteLine($"     relations: {item.NonContainmentDegree} non-containment, {item.RelationDiversity} kinds");
            foreach (var reason in item.Reasons.Take(3))
            {
                Console.WriteLine($"     - {reason}");
            }
        }
    }

    private static void PrintClusters(IReadOnlyList<GraphClusterSummary> clusters)
    {
        Console.WriteLine("Graph clusters");
        if (clusters.Count == 0)
        {
            Console.WriteLine("  none");
            return;
        }

        foreach (var cluster in clusters)
        {
            Console.WriteLine($"  {cluster.Rank}. nodes={cluster.NodeCount}, edges={cluster.EdgeCount}");
            if (cluster.TopNodeKinds.Count > 0)
            {
                Console.WriteLine($"     kinds: {FormatCountsInline(cluster.TopNodeKinds)}");
            }

            if (cluster.TopRelations.Count > 0)
            {
                Console.WriteLine($"     relations: {FormatCountsInline(cluster.TopRelations)}");
            }

            foreach (var node in cluster.RepresentativeNodes.Take(3))
            {
                Console.WriteLine($"     - {node.Label} ({node.Kind})");
            }
        }
    }

    private static void PrintList(string title, IReadOnlyList<string> values)
    {
        Console.WriteLine(title);
        if (values.Count == 0)
        {
            Console.WriteLine("  none");
            return;
        }

        foreach (var value in values)
        {
            Console.WriteLine($"  - {value}");
        }
    }

    private static string FormatCountsInline(IReadOnlyDictionary<string, int> counts)
    {
        return string.Join(", ", counts.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static void PrintEdges(string title, IReadOnlyList<GraphEdge> edges, string currentNodeId)
    {
        Console.WriteLine($"{title}: {edges.Count}");
        foreach (var edge in edges.Take(20))
        {
            var direction = edge.Source == currentNodeId
                ? $"--{edge.Relation}--> {edge.Target}"
                : $"{edge.Source} --{edge.Relation}-->";
            Console.WriteLine($"  {direction} [{edge.Confidence}]");
            if (edge.Evidence is { } evidence)
            {
                Console.WriteLine($"    evidence: {FormatEvidence(evidence)}");
            }
        }

        if (edges.Count > 20)
        {
            Console.WriteLine($"  ... and {edges.Count - 20} more");
        }
    }

    private static string FormatEvidence(GraphEvidence evidence)
    {
        var location = evidence.File is null ? null : FormatLocation(evidence.File, evidence.Line?.ToString());
        if (location is null)
        {
            return evidence.Reason ?? "no evidence detail";
        }

        return evidence.Reason is null ? location : $"{location} {evidence.Reason}";
    }

    private static string? FormatLocation(string? file, string? location)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(location) ? file : $"{file}:{location}";
    }
}

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

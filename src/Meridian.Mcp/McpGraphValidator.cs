using System.Text.Json;
using Meridian.Abstractions;

namespace Meridian.Mcp;

internal static class McpGraphValidator
{
    private static readonly string[] RequiredTopLevelProperties =
    [
        "schema_version",
        "generator",
        "generator_version",
        "nodes",
        "edges",
        "diagnostics"
    ];

    public static void ValidateJsonShape(string json, MeridianMcpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Graph JSON root must be an object.");
        }

        foreach (var propertyName in RequiredTopLevelProperties)
        {
            if (!document.RootElement.TryGetProperty(propertyName, out _))
            {
                throw new InvalidOperationException($"Graph JSON is missing required top-level property '{propertyName}'.");
            }
        }

        RequireString(document.RootElement, "schema_version");
        RequireString(document.RootElement, "generator");
        RequireString(document.RootElement, "generator_version");
        var nodes = RequireArray(document.RootElement, "nodes");
        var edges = RequireArray(document.RootElement, "edges");
        var diagnostics = RequireArray(document.RootElement, "diagnostics");
        EnsureCountWithinLimit("nodes", nodes.GetArrayLength(), options.MaxGraphNodes);
        EnsureCountWithinLimit("edges", edges.GetArrayLength(), options.MaxGraphEdges);
        EnsureCountWithinLimit("diagnostics", diagnostics.GetArrayLength(), options.MaxGraphDiagnostics);
    }

    public static void Validate(GraphDocument graph, MeridianMcpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(graph.SchemaVersion))
        {
            throw new InvalidOperationException("Graph schema version is missing.");
        }

        if (!graph.SchemaVersion.Equals("0.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported graph schema version '{graph.SchemaVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(graph.Generator))
        {
            throw new InvalidOperationException("Graph generator is missing.");
        }

        if (string.IsNullOrWhiteSpace(graph.GeneratorVersion))
        {
            throw new InvalidOperationException("Graph generator version is missing.");
        }

        if (graph.Nodes is null)
        {
            throw new InvalidOperationException("Graph nodes collection is missing.");
        }

        if (graph.Edges is null)
        {
            throw new InvalidOperationException("Graph edges collection is missing.");
        }

        if (graph.Diagnostics is null)
        {
            throw new InvalidOperationException("Graph diagnostics collection is missing.");
        }

        EnsureCountWithinLimit("nodes", graph.Nodes.Count, options.MaxGraphNodes);
        EnsureCountWithinLimit("edges", graph.Edges.Count, options.MaxGraphEdges);
        EnsureCountWithinLimit("diagnostics", graph.Diagnostics.Count, options.MaxGraphDiagnostics);

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            if (node is null)
            {
                throw new InvalidOperationException("Graph contains a null node.");
            }

            if (string.IsNullOrWhiteSpace(node.Id))
            {
                throw new InvalidOperationException("Graph contains a node with a missing id.");
            }

            if (string.IsNullOrWhiteSpace(node.Label))
            {
                throw new InvalidOperationException($"Graph node '{node.Id}' has a missing label.");
            }

            if (string.IsNullOrWhiteSpace(node.Kind))
            {
                throw new InvalidOperationException($"Graph node '{node.Id}' has a missing kind.");
            }

            if (node.Metadata is null)
            {
                throw new InvalidOperationException($"Graph node '{node.Id}' has a missing metadata collection.");
            }

            if (!nodeIds.Add(node.Id))
            {
                throw new InvalidOperationException($"Graph contains duplicate node id '{node.Id}'.");
            }
        }

        foreach (var edge in graph.Edges)
        {
            if (edge is null)
            {
                throw new InvalidOperationException("Graph contains a null edge.");
            }

            if (string.IsNullOrWhiteSpace(edge.Source))
            {
                throw new InvalidOperationException("Graph contains an edge with a missing source.");
            }

            if (string.IsNullOrWhiteSpace(edge.Target))
            {
                throw new InvalidOperationException("Graph contains an edge with a missing target.");
            }

            if (string.IsNullOrWhiteSpace(edge.Relation))
            {
                throw new InvalidOperationException($"Graph edge '{edge.Source}' -> '{edge.Target}' has a missing relation.");
            }

            if (string.IsNullOrWhiteSpace(edge.Confidence))
            {
                throw new InvalidOperationException($"Graph edge '{edge.Source}' -> '{edge.Target}' has a missing confidence.");
            }

            if (edge.Metadata is null)
            {
                throw new InvalidOperationException($"Graph edge '{edge.Source}' -> '{edge.Target}' has a missing metadata collection.");
            }

            if (!nodeIds.Contains(edge.Source))
            {
                throw new InvalidOperationException($"Graph edge source '{edge.Source}' does not match a node id.");
            }

            if (!nodeIds.Contains(edge.Target))
            {
                throw new InvalidOperationException($"Graph edge target '{edge.Target}' does not match a node id.");
            }
        }
    }

    private static void RequireString(JsonElement root, string propertyName)
    {
        var value = root.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException($"Graph JSON property '{propertyName}' must be a non-empty string.");
        }
    }

    private static JsonElement RequireArray(JsonElement root, string propertyName)
    {
        var value = root.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Graph JSON property '{propertyName}' must be an array.");
        }

        return value;
    }

    private static void EnsureCountWithinLimit(string collectionName, int count, int limit)
    {
        if (limit > 0 && count > limit)
        {
            throw new InvalidOperationException($"Graph {collectionName} count is {count}, exceeding configured limit of {limit}.");
        }
    }
}

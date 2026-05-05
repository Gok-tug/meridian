using Meridian.Abstractions;
using Meridian.Exporters.Json;

namespace Meridian.Mcp;

public sealed class McpGraphStore
{
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private McpGraphContext _current;

    public McpGraphStore(McpGraphContext initialContext)
    {
        ArgumentNullException.ThrowIfNull(initialContext);
        Options = initialContext.Options;
        _current = initialContext;
    }

    public MeridianMcpServerOptions Options { get; }

    public McpGraphContext Current => Volatile.Read(ref _current);

    public static async Task<McpGraphStore> CreateAsync(MeridianMcpServerOptions options, CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(options, cancellationToken);
        return new McpGraphStore(context);
    }

    public async Task<McpGraphReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            var previous = Current;
            try
            {
                var next = await LoadContextAsync(Options, cancellationToken);
                Volatile.Write(ref _current, next);
                return new McpGraphReloadResult("ok", previous, next, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new McpGraphReloadResult("reload_failed", previous, previous, exception.Message);
            }
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private static async Task<McpGraphContext> LoadContextAsync(MeridianMcpServerOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.GraphPath);

        var graph = await JsonGraphExporter.ReadAsync(options.GraphPath, cancellationToken);
        Validate(graph);
        var fileLastWriteTime = File.GetLastWriteTimeUtc(options.GraphPath);
        return new McpGraphContext(graph, options, DateTimeOffset.UtcNow, new DateTimeOffset(fileLastWriteTime, TimeSpan.Zero), options.GraphPath);
    }

    private static void Validate(GraphDocument graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (string.IsNullOrWhiteSpace(graph.SchemaVersion))
        {
            throw new InvalidOperationException("Graph schema version is missing.");
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
}

public sealed record McpGraphReloadResult(
    string Status,
    McpGraphContext Previous,
    McpGraphContext Current,
    string? Message);

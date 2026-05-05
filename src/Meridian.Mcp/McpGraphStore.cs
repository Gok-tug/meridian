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
                return new McpGraphReloadResult("ok", previous, next, false, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new McpGraphReloadResult("reload_failed", previous, previous, true, exception.Message);
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

        var json = await File.ReadAllTextAsync(options.GraphPath, cancellationToken);
        McpGraphValidator.ValidateJsonShape(json);
        var graph = JsonGraphExporter.Deserialize(json);
        var fileLastWriteTime = File.GetLastWriteTimeUtc(options.GraphPath);
        return new McpGraphContext(graph, options, DateTimeOffset.UtcNow, new DateTimeOffset(fileLastWriteTime, TimeSpan.Zero), options.GraphPath);
    }
}

public sealed record McpGraphReloadResult(
    string Status,
    McpGraphContext Previous,
    McpGraphContext Current,
    bool PreviousGraphPreserved,
    string? Message);

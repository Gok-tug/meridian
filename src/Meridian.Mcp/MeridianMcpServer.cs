using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Meridian.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Mcp;

public static class MeridianMcpServer
{
    public static async Task RunAsync(MeridianMcpServerOptions serverOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverOptions.GraphPath);

        var store = await McpGraphStore.CreateAsync(serverOptions, cancellationToken);
        await RunAsync(store, serverOptions, cancellationToken);
    }

    public static Task RunAsync(GraphDocument graph, MeridianMcpServerOptions serverOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(serverOptions);

        var context = new McpGraphContext(graph, serverOptions);
        var store = new McpGraphStore(context);
        return RunAsync(store, serverOptions, cancellationToken);
    }

    public static async Task RunAsync(McpGraphStore store, MeridianMcpServerOptions serverOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(serverOptions);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(serverOptions);
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<MeridianGraphToolService>();

        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        serializerOptions.Converters.Add(new JsonStringEnumConverter());

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<MeridianMcpTools>(serializerOptions);

        await builder.Build().RunAsync(cancellationToken);
    }
}

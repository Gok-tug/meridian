using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Abstractions;

namespace Meridian.Exporters.Json;

public static class JsonGraphExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(GraphDocument graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return JsonSerializer.Serialize(graph, Options) + Environment.NewLine;
    }

    public static GraphDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<GraphDocument>(json, Options) ??
            throw new InvalidOperationException("Graph JSON could not be deserialized.");
    }

    public static async Task<GraphDocument> ReadAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        var json = await File.ReadAllTextAsync(inputPath, cancellationToken);
        return Deserialize(json);
    }

    public static async Task WriteAsync(GraphDocument graph, string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, Serialize(graph), cancellationToken);
    }
}

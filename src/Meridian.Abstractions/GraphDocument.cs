using System.Text.Json.Serialization;

namespace Meridian.Abstractions;

public sealed record GraphDocument
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "0.1";

    [JsonPropertyName("generator")]
    public string Generator { get; init; } = "Meridian";

    [JsonPropertyName("generator_version")]
    public string GeneratorVersion { get; init; } = "0.7.0-alpha.1";

    [JsonPropertyName("root")]
    public string? Root { get; init; }

    [JsonPropertyName("nodes")]
    public IReadOnlyList<GraphNode> Nodes { get; init; } = [];

    [JsonPropertyName("edges")]
    public IReadOnlyList<GraphEdge> Edges { get; init; } = [];

    [JsonPropertyName("diagnostics")]
    public IReadOnlyList<GraphDiagnostic> Diagnostics { get; init; } = [];

    [JsonPropertyName("provenance")]
    public GraphProvenance? Provenance { get; init; }
}

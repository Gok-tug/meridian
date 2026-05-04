using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Meridian.Abstractions;

public sealed record GraphNode
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [JsonPropertyName("source_file")]
    public string? SourceFile { get; init; }

    [JsonPropertyName("source_location")]
    public string? SourceLocation { get; init; }

    [JsonPropertyName("metadata")]
    public SortedDictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}

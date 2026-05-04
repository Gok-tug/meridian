using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Meridian.Abstractions;

public sealed record GraphEdge
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("relation")]
    public required string Relation { get; init; }

    [JsonPropertyName("confidence")]
    public required string Confidence { get; init; }

    [JsonPropertyName("confidence_score")]
    public double? ConfidenceScore { get; init; }

    [JsonPropertyName("evidence")]
    public GraphEvidence? Evidence { get; init; }

    [JsonPropertyName("metadata")]
    public SortedDictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}

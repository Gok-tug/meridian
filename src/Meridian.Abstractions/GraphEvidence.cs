using System.Text.Json.Serialization;

namespace Meridian.Abstractions;

public sealed record GraphEvidence
{
    [JsonPropertyName("file")]
    public string? File { get; init; }

    [JsonPropertyName("line")]
    public int? Line { get; init; }

    [JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

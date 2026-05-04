using System.Text.Json.Serialization;

namespace Meridian.Abstractions;

public sealed record GraphDiagnostic
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("source_file")]
    public string? SourceFile { get; init; }

    [JsonPropertyName("source_location")]
    public string? SourceLocation { get; init; }
}

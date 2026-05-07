using System.Text.Json.Serialization;

namespace Meridian.Abstractions;

/// <summary>
/// Captures the repository state at the time the graph was scanned. Consumers (CLI, MCP) compare these
/// values against the current repository state to flag stale graphs without re-running Roslyn analysis.
/// All fields are optional so older graph documents without provenance remain backwards compatible.
/// </summary>
public sealed record GraphProvenance
{
    [JsonPropertyName("generated_at")]
    public DateTimeOffset? GeneratedAt { get; init; }

    [JsonPropertyName("git_commit")]
    public string? GitCommit { get; init; }

    [JsonPropertyName("git_branch")]
    public string? GitBranch { get; init; }

    [JsonPropertyName("git_dirty")]
    public bool? GitDirty { get; init; }
}

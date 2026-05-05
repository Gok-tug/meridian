namespace Meridian.Mcp;

public sealed record MeridianMcpServerOptions
{
    public required string GraphPath { get; init; }

    public int DefaultMaxResults { get; init; } = 50;

    public int MaxResultsLimit { get; init; } = 100;

    public int DefaultMaxDepth { get; init; } = 1;

    public int MaxDepthLimit { get; init; } = 8;

    public long MaxGraphJsonBytes { get; init; } = 64L * 1024L * 1024L;

    public int MaxGraphNodes { get; init; } = 100_000;

    public int MaxGraphEdges { get; init; } = 250_000;

    public int MaxGraphDiagnostics { get; init; } = 50_000;

    public bool IncludeEvidenceByDefault { get; init; }
}

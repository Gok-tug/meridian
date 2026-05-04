namespace Meridian.Mcp;

public sealed record MeridianMcpServerOptions
{
    public required string GraphPath { get; init; }

    public int DefaultMaxResults { get; init; } = 50;

    public int MaxResultsLimit { get; init; } = 100;

    public int DefaultMaxDepth { get; init; } = 1;

    public int MaxDepthLimit { get; init; } = 8;

    public bool IncludeEvidenceByDefault { get; init; }
}

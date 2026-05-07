using Meridian.Mcp.Responses;

namespace Meridian.Mcp;

/// <summary>
/// Surfaces filename-based documentation hints when planning a feature.
/// Implementations must not read or index file contents; only file paths and modification times.
/// </summary>
public interface IDocHintProvider
{
    IReadOnlyList<DocHintDto> GetHints(string goal, IReadOnlyList<string> terms, int maxHints);
}

internal sealed class NullDocHintProvider : IDocHintProvider
{
    public static readonly NullDocHintProvider Instance = new();

    private NullDocHintProvider()
    {
    }

    public IReadOnlyList<DocHintDto> GetHints(string goal, IReadOnlyList<string> terms, int maxHints)
    {
        return [];
    }
}

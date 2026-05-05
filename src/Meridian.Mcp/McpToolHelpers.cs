using Meridian.Core;
using Meridian.Mcp.Responses;

namespace Meridian.Mcp;

internal static class McpToolHelpers
{
    public static bool IsBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value);
    }

    public static string EscapeSuggestionValue(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    public static GraphNodeResolution ResolveNodeForMcp(McpGraphContext context, string query)
    {
        var limit = context.ClampMaxResults(null);
        return context.Query.ResolveNode(query, limit + 1);
    }

    public static (IReadOnlyList<CandidateDto> Items, bool Truncated) CapCandidates(McpGraphContext context, GraphNodeResolution resolution)
    {
        var limit = context.ClampMaxResults(null);
        return Cap(resolution.Candidates.Select(CandidateDto.From), limit);
    }

    public static (IReadOnlyList<T> Items, bool Truncated) Cap<T>(IEnumerable<T> values, int limit)
    {
        var items = values.Take(limit + 1).ToArray();
        return items.Length > limit
            ? (items.Take(limit).ToArray(), true)
            : (items, false);
    }
}

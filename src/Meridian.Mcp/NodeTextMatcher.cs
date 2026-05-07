using Meridian.Abstractions;

namespace Meridian.Mcp;

internal static class NodeTextMatcher
{
    public static bool MatchesText(GraphNode node, string? text, QueryGraphMatchKind matchKind)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var trimmed = text!.Trim();
        return matchKind switch
        {
            QueryGraphMatchKind.Exact => MatchesExact(node, trimmed),
            QueryGraphMatchKind.Prefix => MatchesPrefix(node, trimmed),
            QueryGraphMatchKind.Suffix => MatchesSuffix(node, trimmed),
            QueryGraphMatchKind.Token => MatchesToken(node, trimmed),
            _ => MatchesContains(node, trimmed)
        };
    }

    public static bool IsAnonymousTypeNode(GraphNode node)
    {
        return ContainsAnonymousMarker(node.Symbol) ||
            ContainsAnonymousMarker(node.Label) ||
            ContainsAnonymousMarker(node.Id);
    }

    private static bool MatchesContains(GraphNode node, string text)
    {
        return node.Id.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            node.Label.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            (node.Symbol?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool MatchesExact(GraphNode node, string text)
    {
        return node.Id.Equals(text, StringComparison.OrdinalIgnoreCase) ||
            node.Label.Equals(text, StringComparison.OrdinalIgnoreCase) ||
            (node.Symbol?.Equals(text, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool MatchesPrefix(GraphNode node, string text)
    {
        return node.Id.StartsWith(text, StringComparison.OrdinalIgnoreCase) ||
            node.Label.StartsWith(text, StringComparison.OrdinalIgnoreCase) ||
            (node.Symbol?.StartsWith(text, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool MatchesSuffix(GraphNode node, string text)
    {
        return node.Id.EndsWith(text, StringComparison.OrdinalIgnoreCase) ||
            node.Label.EndsWith(text, StringComparison.OrdinalIgnoreCase) ||
            (node.Symbol?.EndsWith(text, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool MatchesToken(GraphNode node, string text)
    {
        return ContainsTokenBoundary(node.Id, text) ||
            ContainsTokenBoundary(node.Label, text) ||
            ContainsTokenBoundary(node.Symbol, text);
    }

    private static bool ContainsTokenBoundary(string? source, string token)
    {
        if (string.IsNullOrEmpty(source))
        {
            return false;
        }

        var index = 0;
        while (index <= source.Length - token.Length)
        {
            var hit = source.IndexOf(token, index, StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                return false;
            }

            if (IsTokenBoundary(source, hit, hit + token.Length))
            {
                return true;
            }

            index = hit + 1;
        }

        return false;
    }

    private static bool IsTokenBoundary(string source, int start, int end)
    {
        var leftBoundary = start == 0 || !IsIdentifierPart(source[start - 1]);
        var rightBoundary = end >= source.Length || !IsIdentifierPart(source[end]);
        return leftBoundary && rightBoundary;
    }

    private static bool IsIdentifierPart(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private static bool ContainsAnonymousMarker(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value.Contains("<>f__AnonymousType", StringComparison.Ordinal) ||
            value.Contains("AnonymousType<", StringComparison.Ordinal) ||
            value.StartsWith("<>", StringComparison.Ordinal);
    }
}

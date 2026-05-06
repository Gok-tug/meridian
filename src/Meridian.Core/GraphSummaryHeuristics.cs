using Meridian.Abstractions;

namespace Meridian.Core;

public static class GraphSummaryHeuristics
{
    private const int MinimumTermLength = 2;

    public static readonly string[] ExtensionPointTerms =
    [
        "Mode",
        "Strategy",
        "Policy",
        "Factory",
        "Registry",
        "Resolver",
        "Selector",
        "Executor",
        "Orchestrator",
        "Dispatcher",
        "Handler"
    ];

    public static readonly string[] CentralNameTerms =
    [
        "Service",
        "Repository",
        "Factory",
        "Registry",
        "Resolver",
        "Strategy",
        "Policy",
        "Orchestrator",
        "Dispatcher",
        "Handler"
    ];

    public static readonly string[] ImportantRelations =
    [
        GraphRelations.Calls,
        GraphRelations.Uses,
        GraphRelations.Reads,
        GraphRelations.Writes,
        GraphRelations.Queries,
        GraphRelations.Sends,
        GraphRelations.Publishes,
        GraphRelations.Reflects,
        GraphRelations.Injects,
        GraphRelations.RegisteredAs,
        GraphRelations.HandledBy,
        GraphRelations.ImplementedBy
    ];

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "and",
        "add",
        "create",
        "for",
        "feature",
        "implement",
        "implementation",
        "in",
        "new",
        "of",
        "on",
        "or",
        "should",
        "support",
        "the",
        "to",
        "use",
        "using",
        "where",
        "with",
        "without"
    };

    public static IReadOnlyList<string> TokenizeFeatureTerms(string goal, string[]? explicitTerms)
    {
        return SplitFeatureText(goal)
            .Concat((explicitTerms ?? []).SelectMany(SplitFeatureText))
            .Select(term => term.ToLowerInvariant())
            .Where(term => term.Length >= MinimumTermLength && !StopWords.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static string? TryGetExtensionPointTerm(GraphNode node)
    {
        return ExtensionPointTerms.FirstOrDefault(term => NodeContains(node, term));
    }

    public static string? TryGetCentralNameTerm(GraphNode node)
    {
        return CentralNameTerms.FirstOrDefault(term => NodeContains(node, term));
    }

    public static bool TermMatchesNode(GraphNode node, string term)
    {
        return NodeContains(node, term) ||
            node.SourceFile?.Contains(term, StringComparison.OrdinalIgnoreCase) == true ||
            node.Metadata.Values.Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static int FeaturePlanningKindBoost(GraphNode node, out string? reason)
    {
        switch (node.Kind)
        {
            case GraphNodeKinds.Enum:
                reason = "Enum nodes often define selectable modes.";
                return 24;
            case GraphNodeKinds.Property:
            case GraphNodeKinds.Field:
                reason = "Member node can indicate persisted, routed, or domain state.";
                return 14;
            case GraphNodeKinds.Type when node.Metadata.TryGetValue("type_kind", out var typeKind) && typeKind.Equals("interface", StringComparison.OrdinalIgnoreCase):
                reason = "Interface type is a likely extension point.";
                return 18;
            case GraphNodeKinds.Type:
            case GraphNodeKinds.DbContext:
            case GraphNodeKinds.MediatRHandler:
                reason = "Type node may own extension-point behavior.";
                return 8;
            case GraphNodeKinds.Method:
                reason = "Method node may contain dispatch or orchestration logic.";
                return 4;
            default:
                reason = null;
                return 0;
        }
    }

    public static string EscapeSuggestionValue(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static bool NodeContains(GraphNode node, string value)
    {
        return node.Id.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            node.Label.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            node.Symbol?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static IEnumerable<string> SplitFeatureText(string text)
    {
        foreach (var word in SplitWords(text))
        {
            yield return word;
            foreach (var part in SplitPascalCase(word))
            {
                yield return part;
            }
        }
    }

    private static IEnumerable<string> SplitWords(string text)
    {
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i]))
            {
                if (start < 0)
                {
                    start = i;
                }

                continue;
            }

            if (start >= 0)
            {
                yield return text[start..i];
                start = -1;
            }
        }

        if (start >= 0)
        {
            yield return text[start..];
        }
    }

    private static IEnumerable<string> SplitPascalCase(string value)
    {
        if (value.Length == 0)
        {
            yield break;
        }

        var start = 0;
        for (var i = 1; i < value.Length; i++)
        {
            var current = value[i];
            var previous = value[i - 1];
            var nextStartsWord = char.IsUpper(current) &&
                (char.IsLower(previous) || i + 1 < value.Length && char.IsLower(value[i + 1]));
            if (!nextStartsWord)
            {
                continue;
            }

            if (i > start)
            {
                yield return value[start..i];
            }

            start = i;
        }

        if (start == 0)
        {
            yield break;
        }

        yield return value[start..];
    }
}

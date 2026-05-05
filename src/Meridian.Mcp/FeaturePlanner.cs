using Meridian.Abstractions;
using Meridian.Core;
using Meridian.Mcp.Responses;
using static Meridian.Mcp.McpToolHelpers;

namespace Meridian.Mcp;

internal sealed class FeaturePlanner
{
    private const int MaximumMatchedTerms = 5;
    private const int MinimumTermLength = 2;
    private const int MaximumSeedDistance = 2;
    private const int TermMatchScore = 12;
    private const int ExtensionPointScore = 16;
    private const int RelationCentralityScorePerRelation = 2;
    private const int MaximumRelationCentralityScore = 12;
    private const int ExactSeedScore = 35;
    private const int DirectSeedNeighborScore = 25;
    private const int NearSeedScore = 12;
    private const int EnumKindScore = 24;
    private const int MemberKindScore = 14;
    private const int InterfaceKindScore = 18;
    private const int TypeKindScore = 8;
    private const int MethodKindScore = 4;

    private static readonly string[] ExtensionPointTerms =
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

    public FeaturePlanResponse Plan(McpGraphContext context, string? goal, string[]? seedSymbols, string[]? terms, int? maxResults)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsBlank(goal))
        {
            return new FeaturePlanResponse(
                "invalid_input",
                MeridianMcpMessages.StaleGraphNote,
                goal ?? string.Empty,
                [],
                [],
                [],
                "Parameter 'goal' is required.",
                Message: "Parameter 'goal' is required.");
        }

        var limit = context.ClampMaxResults(maxResults);
        var planTerms = TokenizeFeatureTerms(goal!, terms).ToArray();
        var seedResolutions = ResolveSeeds(context, seedSymbols).ToArray();
        var foundSeeds = seedResolutions
            .Where(seed => seed.Node is not null)
            .Select(seed => context.NodesById[seed.Node!.Id])
            .ToArray();
        var seedDistances = ComputeSeedDistances(context, foundSeeds);
        var candidates = context.Graph.Nodes
            .Select(node => ScoreCandidate(context, node, planTerms, seedDistances))
            .OfType<FeatureCandidate>()
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Node.SourceFile, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Node.Label, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Node.Id, StringComparer.Ordinal)
            .ToArray();
        var capped = Cap(candidates, limit);
        var editPoints = capped.Items
            .Select((candidate, index) => new FeaturePlanCandidateDto(
                index + 1,
                candidate.Score,
                NodeDto.From(candidate.Node),
                candidate.Reasons,
                SuggestedQueries(candidate.Node)))
            .ToArray();

        return new FeaturePlanResponse(
            editPoints.Length == 0 ? "no_results" : "ok",
            MeridianMcpMessages.StaleGraphNote,
            goal!,
            planTerms,
            seedResolutions,
            editPoints,
            Limitation(context, planTerms),
            capped.Truncated,
            capped.Truncated ? MeridianMcpMessages.TruncationNote(limit) : null,
            editPoints.Length == 0 ? "No ranked edit points were found in the loaded Meridian graph. This does not prove no source-code extension point exists." : null);
    }

    private static IReadOnlyList<string> SuggestedQueries(GraphNode node)
    {
        var id = EscapeSuggestionValue(node.Id);
        return
        [
            $"get_symbol_summary idOrLabel:\"{id}\"",
            $"get_neighbors idOrLabel:\"{id}\" direction:\"Both\" depth:1 excludeRelations:[\"contains\"]"
        ];
    }

    private static SeedResolutionDto[] ResolveSeeds(McpGraphContext context, string[]? seedSymbols)
    {
        if (seedSymbols is null)
        {
            return [];
        }

        return seedSymbols
            .Where(seed => !IsBlank(seed))
            .Select(seed => seed.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .Select(seed => ResolveSeed(context, seed))
            .ToArray();
    }

    private static SeedResolutionDto ResolveSeed(McpGraphContext context, string seed)
    {
        var resolution = ResolveNodeForMcp(context, seed);
        if (resolution.Status == GraphNodeResolutionStatus.Found)
        {
            return new SeedResolutionDto(seed, "found", NodeDto.From(resolution.Node!));
        }

        if (resolution.Status == GraphNodeResolutionStatus.Ambiguous)
        {
            var candidates = CapCandidates(context, resolution);
            return new SeedResolutionDto(
                seed,
                "ambiguous",
                Candidates: candidates.Items,
                Truncated: candidates.Truncated,
                TruncationNote: candidates.Truncated ? MeridianMcpMessages.TruncationNote(context.ClampMaxResults(null)) : null,
                Message: $"Seed symbol '{seed}' is ambiguous. More precise seed symbols improve ranking.");
        }

        return new SeedResolutionDto(
            seed,
            "not_found",
            Message: $"No seed node in the loaded Meridian graph matched '{seed}'. This does not prove the symbol is absent from source.");
    }

    private static IReadOnlyDictionary<string, int> ComputeSeedDistances(McpGraphContext context, IEnumerable<GraphNode> seedNodes)
    {
        var distances = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<(string NodeId, int Distance)>();
        foreach (var seed in seedNodes.OrderBy(seed => seed.Id, StringComparer.Ordinal))
        {
            if (distances.TryAdd(seed.Id, 0))
            {
                queue.Enqueue((seed.Id, 0));
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Distance >= MaximumSeedDistance)
            {
                continue;
            }

            foreach (var edge in context.GetEdges(current.NodeId, GraphDirection.Both))
            {
                var nextNodeId = edge.Source == current.NodeId ? edge.Target : edge.Source;
                if (distances.TryAdd(nextNodeId, current.Distance + 1))
                {
                    queue.Enqueue((nextNodeId, current.Distance + 1));
                }
            }
        }

        return distances;
    }

    private static FeatureCandidate? ScoreCandidate(
        McpGraphContext context,
        GraphNode node,
        IReadOnlyList<string> terms,
        IReadOnlyDictionary<string, int> seedDistances)
    {
        var score = 0;
        var reasons = new List<string>();
        var matchedTerms = terms
            .Where(term => TermMatchesNode(node, term))
            .Take(MaximumMatchedTerms)
            .ToArray();
        if (matchedTerms.Length > 0)
        {
            score += matchedTerms.Length * TermMatchScore;
            reasons.Add($"Matches term(s): {string.Join(", ", matchedTerms)}.");
        }

        if (TryGetExtensionPointTerm(node) is { } extensionPointTerm)
        {
            score += ExtensionPointScore;
            reasons.Add($"Name suggests extension point: {extensionPointTerm}.");
        }

        var kindBoost = KindBoost(node, out var kindReason);
        if (kindBoost > 0)
        {
            score += kindBoost;
            reasons.Add(kindReason!);
        }

        var relationCount = context.GetEdges(node.Id, GraphDirection.Both)
            .Count(edge => !edge.Relation.Equals(GraphRelations.Contains, StringComparison.OrdinalIgnoreCase));
        if (relationCount > 0)
        {
            var centralityBoost = Math.Min(relationCount * RelationCentralityScorePerRelation, MaximumRelationCentralityScore);
            score += centralityBoost;
            reasons.Add($"Has {relationCount} non-containment relation(s) in the graph.");
        }

        if (seedDistances.TryGetValue(node.Id, out var seedDistance))
        {
            var seedBoost = seedDistance switch
            {
                0 => ExactSeedScore,
                1 => DirectSeedNeighborScore,
                _ => NearSeedScore
            };
            score += seedBoost;
            reasons.Add(seedDistance == 0 ? "Resolved seed symbol." : $"Within graph distance {seedDistance} of a resolved seed.");
        }

        return score == 0 ? null : new FeatureCandidate(node, score, reasons);
    }

    private static int KindBoost(GraphNode node, out string? reason)
    {
        switch (node.Kind)
        {
            case GraphNodeKinds.Enum:
                reason = "Enum nodes often define selectable modes.";
                return EnumKindScore;
            case GraphNodeKinds.Property:
            case GraphNodeKinds.Field:
                reason = "Member node can indicate persisted, routed, or domain state.";
                return MemberKindScore;
            case GraphNodeKinds.Type when node.Metadata.TryGetValue("type_kind", out var typeKind) && typeKind.Equals("interface", StringComparison.OrdinalIgnoreCase):
                reason = "Interface type is a likely extension point.";
                return InterfaceKindScore;
            case GraphNodeKinds.Type:
            case GraphNodeKinds.DbContext:
            case GraphNodeKinds.MediatRHandler:
                reason = "Type node may own extension-point behavior.";
                return TypeKindScore;
            case GraphNodeKinds.Method:
                reason = "Method node may contain dispatch or orchestration logic.";
                return MethodKindScore;
            default:
                reason = null;
                return 0;
        }
    }

    private static string? TryGetExtensionPointTerm(GraphNode node)
    {
        return ExtensionPointTerms.FirstOrDefault(term =>
            node.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            node.Label.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            node.Symbol?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static IReadOnlyList<string> TokenizeFeatureTerms(string goal, string[]? explicitTerms)
    {
        return SplitFeatureText(goal)
            .Concat((explicitTerms ?? []).SelectMany(SplitFeatureText))
            .Select(term => term.ToLowerInvariant())
            .Where(term => term.Length > MinimumTermLength - 1 && !StopWords.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
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

    private static bool TermMatchesNode(GraphNode node, string term)
    {
        return node.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            node.Label.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            node.Symbol?.Contains(term, StringComparison.OrdinalIgnoreCase) == true ||
            node.SourceFile?.Contains(term, StringComparison.OrdinalIgnoreCase) == true ||
            node.Metadata.Values.Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string Limitation(McpGraphContext context, IReadOnlyList<string> terms)
    {
        var absentTerms = terms
            .Where(term => !context.Graph.Nodes.Any(node => TermMatchesNode(node, term)))
            .ToArray();
        if (absentTerms.Length == 0)
        {
            return "Ranked deterministically from the loaded Meridian graph; verify source before editing.";
        }

        return $"Term(s) not present in the loaded Meridian graph: {string.Join(", ", absentTerms)}. Ranked edit points are existing graph extension points; this does not prove the terms are absent from source.";
    }

    private sealed record FeatureCandidate(GraphNode Node, int Score, IReadOnlyList<string> Reasons);
}

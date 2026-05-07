using Meridian.Abstractions;
using Meridian.Core;
using Meridian.Mcp.Responses;
using static Meridian.Mcp.McpToolHelpers;

namespace Meridian.Mcp;

internal sealed class FeaturePlanner
{
    private const int MaximumMatchedTerms = 5;
    private const int MaximumSeedDistance = 2;
    private const int ExtensionPointScore = 16;
    private const int RelationCentralityScorePerRelation = 2;
    private const int MaximumRelationCentralityScore = 12;
    private const int ExactSeedScore = 35;
    private const int DirectSeedNeighborScore = 25;
    private const int NearSeedScore = 12;

    private static readonly int[] TermStrengthScores =
    [
        0,
        2,
        6,
        12,
        18
    ];

    private readonly IDocHintProvider _docHintProvider;

    public FeaturePlanner()
        : this(NullDocHintProvider.Instance)
    {
    }

    public FeaturePlanner(IDocHintProvider docHintProvider)
    {
        ArgumentNullException.ThrowIfNull(docHintProvider);
        _docHintProvider = docHintProvider;
    }

    public FeaturePlanResponse Plan(McpGraphContext context, string? goal, string[]? seedSymbols, string[]? terms, int? maxResults, string? verbosity = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!FeaturePlanVerbosityParser.TryParse(verbosity, out var verbosityLevel))
        {
            return new FeaturePlanResponse(
                "invalid_input",
                MeridianMcpMessages.StaleGraphNote,
                goal ?? string.Empty,
                [],
                [],
                [],
                "Parameter 'verbosity' must be compact, standard, or detailed.",
                Message: "Parameter 'verbosity' must be compact, standard, or detailed.",
                Verbosity: verbosity);
        }

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
                Message: "Parameter 'goal' is required.",
                Verbosity: verbosityLevel.ToWireValue());
        }

        var limit = ClampLimitForVerbosity(context, maxResults, verbosityLevel);
        var planTerms = GraphSummaryHeuristics.TokenizeFeatureTerms(goal!, terms).ToArray();
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
            .Select((candidate, index) => CreateCandidateDto(candidate, index, verbosityLevel))
            .ToArray();
        var docHints = _docHintProvider.GetHints(goal!, planTerms, MaximumDocHintsForVerbosity(verbosityLevel));

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
            editPoints.Length == 0 ? "No ranked edit points were found in the loaded Meridian graph. This does not prove no source-code extension point exists." : null,
            verbosityLevel.ToWireValue(),
            docHints);
    }

    private static int ClampLimitForVerbosity(McpGraphContext context, int? maxResults, FeaturePlanVerbosity verbosity)
    {
        var requested = maxResults ?? DefaultLimitForVerbosity(verbosity);
        return context.ClampMaxResults(requested);
    }

    private static int DefaultLimitForVerbosity(FeaturePlanVerbosity verbosity)
    {
        return verbosity switch
        {
            FeaturePlanVerbosity.Compact => 5,
            FeaturePlanVerbosity.Detailed => 25,
            _ => 10
        };
    }

    private static int MaximumDocHintsForVerbosity(FeaturePlanVerbosity verbosity)
    {
        return verbosity switch
        {
            FeaturePlanVerbosity.Compact => 2,
            FeaturePlanVerbosity.Detailed => 8,
            _ => 4
        };
    }

    private static FeaturePlanCandidateDto CreateCandidateDto(FeatureCandidate candidate, int index, FeaturePlanVerbosity verbosity)
    {
        return verbosity switch
        {
            FeaturePlanVerbosity.Compact => new FeaturePlanCandidateDto(
                index + 1,
                candidate.Score,
                NodeDto.From(candidate.Node),
                [],
                [],
                candidate.Breakdown),
            FeaturePlanVerbosity.Detailed => new FeaturePlanCandidateDto(
                index + 1,
                candidate.Score,
                NodeDto.From(candidate.Node),
                candidate.Reasons,
                SuggestedQueries(candidate.Node),
                candidate.Breakdown),
            _ => new FeaturePlanCandidateDto(
                index + 1,
                candidate.Score,
                NodeDto.From(candidate.Node),
                candidate.Reasons,
                SuggestedQueries(candidate.Node),
                candidate.Breakdown)
        };
    }

    private static IReadOnlyList<string> SuggestedQueries(GraphNode node)
    {
        var id = GraphSummaryHeuristics.EscapeSuggestionValue(node.Id);
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
        var reasons = new List<string>();
        var termContributions = new List<TermMatchContributionDto>();
        var termScore = 0;

        foreach (var term in terms.Take(MaximumMatchedTerms))
        {
            var strength = GraphSummaryHeuristics.ScoreTermMatch(node, term);
            if (strength <= 0)
            {
                continue;
            }

            var addition = TermStrengthScores[strength];
            termScore += addition;
            termContributions.Add(new TermMatchContributionDto(term, strength, addition));
        }

        if (termContributions.Count > 0)
        {
            var labels = termContributions
                .Select(contribution => $"{contribution.Term}({StrengthLabel(contribution.Strength)})");
            reasons.Add($"Matches term(s): {string.Join(", ", labels)}.");
        }

        var extensionPointScore = 0;
        if (GraphSummaryHeuristics.TryGetExtensionPointTerm(node) is { } extensionPointTerm)
        {
            extensionPointScore = ExtensionPointScore;
            reasons.Add($"Name suggests extension point: {extensionPointTerm}.");
        }

        var kindBoost = GraphSummaryHeuristics.FeaturePlanningKindBoost(node, out var kindReason);
        if (kindBoost > 0 && kindReason is not null)
        {
            reasons.Add(kindReason);
        }

        var relationCount = context.GetEdges(node.Id, GraphDirection.Both)
            .Count(edge => !edge.Relation.Equals(GraphRelations.Contains, StringComparison.OrdinalIgnoreCase));
        var centralityBoost = 0;
        if (relationCount > 0)
        {
            centralityBoost = Math.Min(relationCount * RelationCentralityScorePerRelation, MaximumRelationCentralityScore);
            reasons.Add($"Has {relationCount} non-containment relation(s) in the graph.");
        }

        var seedBoost = 0;
        if (seedDistances.TryGetValue(node.Id, out var seedDistance))
        {
            seedBoost = seedDistance switch
            {
                0 => ExactSeedScore,
                1 => DirectSeedNeighborScore,
                _ => NearSeedScore
            };
            reasons.Add(seedDistance == 0 ? "Resolved seed symbol." : $"Within graph distance {seedDistance} of a resolved seed.");
        }

        var totalScore = termScore + extensionPointScore + kindBoost + centralityBoost + seedBoost;
        if (totalScore == 0)
        {
            return null;
        }

        var breakdown = new FeaturePlanScoreBreakdownDto(
            termScore,
            extensionPointScore,
            kindBoost,
            centralityBoost,
            seedBoost,
            termContributions);
        return new FeatureCandidate(node, totalScore, reasons, breakdown);
    }

    private static string StrengthLabel(int strength)
    {
        return strength switch
        {
            4 => "exact",
            3 => "token",
            2 => "substring",
            1 => "metadata",
            _ => "weak"
        };
    }

    private static string Limitation(McpGraphContext context, IReadOnlyList<string> terms)
    {
        var absentTerms = terms
            .Where(term => !context.Graph.Nodes.Any(node => GraphSummaryHeuristics.TermMatchesNode(node, term)))
            .ToArray();
        if (absentTerms.Length == 0)
        {
            return "Ranked deterministically from the loaded Meridian graph; verify source before editing.";
        }

        return $"Term(s) not present in the loaded Meridian graph: {string.Join(", ", absentTerms)}. Ranked edit points are existing graph extension points; this does not prove the terms are absent from source.";
    }

    private sealed record FeatureCandidate(GraphNode Node, int Score, IReadOnlyList<string> Reasons, FeaturePlanScoreBreakdownDto Breakdown);
}

using System.Collections.ObjectModel;
using Meridian.Abstractions;
using Meridian.Core;

namespace Meridian.Mcp.Responses;

public sealed record SchemaResponse(
    string Status,
    string StaleGraphNote,
    GraphMetadataDto Graph,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> NodeKindsPresent,
    IReadOnlyList<string> RelationsPresent,
    IReadOnlyList<string> KnownNodeKinds,
    IReadOnlyList<string> KnownRelations,
    IReadOnlyDictionary<string, int>? NodeKindCounts = null,
    IReadOnlyDictionary<string, int>? RelationCounts = null);

public sealed record NodeResponse(
    string Status,
    string StaleGraphNote,
    NodeDto? Node = null,
    IReadOnlyList<CandidateDto>? Candidates = null,
    bool Truncated = false,
    string? TruncationNote = null,
    string? Message = null);

public sealed record GraphSearchResponse(
    string Status,
    string StaleGraphNote,
    IReadOnlyList<NodeDto> Nodes,
    IReadOnlyList<EdgeDto> Edges,
    bool Truncated,
    string? TruncationNote,
    string? Limitation = null,
    IReadOnlyList<CandidateDto>? Candidates = null,
    string? Message = null);

public sealed record PathResponse(
    string Status,
    string StaleGraphNote,
    PathDto? Path = null,
    IReadOnlyList<CandidateDto>? Candidates = null,
    bool Truncated = false,
    string? TruncationNote = null,
    string? Message = null,
    string? Limitation = null);

public sealed record ReloadGraphResponse(
    string Status,
    string StaleGraphNote,
    string GraphPath,
    int PreviousNodeCount,
    int PreviousEdgeCount,
    int NodeCount,
    int EdgeCount,
    string GeneratorVersion,
    DateTimeOffset LoadedAt,
    DateTimeOffset? FileLastWriteTime = null,
    bool PreviousGraphPreserved = false,
    string? Message = null);

public sealed record GraphMetadataDto(
    string SchemaVersion,
    string Generator,
    string GeneratorVersion,
    string? Root,
    int NodeCount,
    int EdgeCount,
    int DiagnosticCount);

public sealed record NodeDto(
    string Id,
    string Label,
    string Kind,
    string? Symbol,
    string? SourceFile,
    string? SourceLocation,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static NodeDto From(GraphNode node)
    {
        return new NodeDto(
            node.Id,
            node.Label,
            node.Kind,
            node.Symbol,
            node.SourceFile,
            node.SourceLocation,
            new ReadOnlyDictionary<string, string>(node.Metadata));
    }
}

public sealed record EdgeDto(
    string Source,
    string Target,
    string Relation,
    string Confidence,
    double? ConfidenceScore,
    EvidenceDto? Evidence,
    IReadOnlyDictionary<string, string> Metadata,
    string? SourceLabel = null,
    string? TargetLabel = null)
{
    public static EdgeDto From(GraphEdge edge, IReadOnlyDictionary<string, GraphNode> nodesById, bool includeEvidence = true)
    {
        nodesById.TryGetValue(edge.Source, out var sourceNode);
        nodesById.TryGetValue(edge.Target, out var targetNode);
        return new EdgeDto(
            edge.Source,
            edge.Target,
            edge.Relation,
            edge.Confidence,
            edge.ConfidenceScore,
            includeEvidence && edge.Evidence is not null ? EvidenceDto.From(edge.Evidence) : null,
            new ReadOnlyDictionary<string, string>(edge.Metadata),
            sourceNode?.Label,
            targetNode?.Label);
    }
}

public sealed record EvidenceDto(string? File, int? Line, string? Symbol, string? Reason)
{
    public static EvidenceDto From(GraphEvidence evidence)
    {
        return new EvidenceDto(evidence.File, evidence.Line, evidence.Symbol, evidence.Reason);
    }
}

public sealed record CandidateDto(string Id, string Label, string Kind, string? Symbol, int Score)
{
    public static CandidateDto From(GraphNodeMatch match)
    {
        return new CandidateDto(match.Node.Id, match.Node.Label, match.Node.Kind, match.Node.Symbol, match.Score);
    }
}

public sealed record PathDto(
    NodeDto Source,
    NodeDto Target,
    IReadOnlyList<PathSegmentDto> Segments,
    int EdgeCount);

public sealed record PathSegmentDto(NodeDto Source, EdgeDto Edge, NodeDto Target);

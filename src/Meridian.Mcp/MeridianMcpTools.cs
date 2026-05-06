using System.ComponentModel;
using Meridian.Mcp.Responses;
using ModelContextProtocol.Server;

namespace Meridian.Mcp;

[McpServerToolType]
public sealed class MeridianMcpTools
{
    private const string SchemaNote = "This graph is precomputed. If source code changes, MCP results will not reflect those changes until meridian scan is run again and the running MCP server is reloaded with reload_graph or restarted. Available node kinds include project, type, method, enum, enum_member, property, field, endpoint, diagnostic, dbcontext, mediatr_request, mediatr_notification, mediatr_handler. Available relations include contains, calls, uses, reads, injects, registered_as, implemented_by, handled_by, sends, publishes, queries, writes, reflects.";
    private const string SeeSchemaNote = "See get_schema for graph freshness and schema notes.";

    private readonly MeridianGraphToolService _service;

    public MeridianMcpTools(MeridianGraphToolService service)
    {
        _service = service;
    }

    [McpServerTool(Name = "get_schema", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Return graph metadata, available node kinds, available edge relations, and tool names. " + SchemaNote)]
    public SchemaResponse GetSchema()
    {
        return _service.GetSchema();
    }

    [McpServerTool(Name = "reload_graph", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Reload the configured graph.json file into this running MCP server. This does not run Roslyn/MSBuild or execute source code; run meridian scan first when source files change. " + SeeSchemaNote)]
    public Task<ReloadGraphResponse> ReloadGraph(CancellationToken cancellationToken = default)
    {
        return _service.ReloadGraphAsync(cancellationToken);
    }

    [McpServerTool(Name = "query_graph", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Typed graph search; do not pass a custom DSL or natural-language question. Use JSON parameters such as text, nodeKind, relation, direction, source, target, and maxResults. " + SeeSchemaNote)]
    public GraphSearchResponse QueryGraph(
        [Description("Optional text contained in node id, label, or symbol. This is not a query language.")] string? text = null,
        [Description("Optional node kind filter. Use get_schema first to see values present in this graph.")] string? nodeKind = null,
        [Description("Optional edge relation filter. Use get_schema first to see values present in this graph.")] string? relation = null,
        [Description("Edge direction for relation-aware filtering: Incoming, Outgoing, or Both.")] GraphDirection direction = GraphDirection.Both,
        [Description("Optional source node id, label, or symbol for edge filtering.")] string? source = null,
        [Description("Optional target node id, label, or symbol for edge filtering.")] string? target = null,
        [Description("Maximum returned nodes and edges. The server caps this to protect the agent context window.")] int? maxResults = null,
        [Description("Include evidence file, line, symbol, and reason for returned edges. Defaults to false for compact bulk responses.")] bool? includeEvidence = null,
        [Description("Optional edge relations to exclude, such as contains, before result capping.")] string[]? excludeRelations = null)
    {
        return _service.QueryGraphWithOptions(text, nodeKind, relation, direction, source, target, maxResults, includeEvidence, excludeRelations);
    }

    [McpServerTool(Name = "get_node", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Resolve one graph node by exact id, label, or symbol. Ambiguous queries return candidates instead of selecting silently. " + SeeSchemaNote)]
    public NodeResponse GetNode([Description("Graph node id, label, or symbol.")] string idOrLabel)
    {
        return _service.GetNode(idOrLabel);
    }

    [McpServerTool(Name = "get_neighbors", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Return bounded incoming, outgoing, or both-direction neighbors for one resolved node. Results are horizontally capped and include TRUNCATED guidance when the cap is reached. " + SeeSchemaNote)]
    public GraphSearchResponse GetNeighbors(
        [Description("Graph node id, label, or symbol.")] string idOrLabel,
        [Description("Neighbor direction: Incoming, Outgoing, or Both.")] GraphDirection direction = GraphDirection.Both,
        [Description("Traversal depth. The server caps this to avoid huge traversals.")] int? depth = null,
        [Description("Optional edge relation filter such as calls, sends, handled_by, injects, or registered_as.")] string? relation = null,
        [Description("Maximum returned nodes and edges. The server caps this to protect the agent context window.")] int? maxResults = null,
        [Description("Include evidence file, line, symbol, and reason for returned edges. Defaults to false for compact bulk responses.")] bool? includeEvidence = null,
        [Description("Optional edge relations to exclude, such as contains, before traversal and result capping.")] string[]? excludeRelations = null)
    {
        return _service.GetNeighborsWithOptions(idOrLabel, direction, depth, relation, maxResults, includeEvidence, excludeRelations);
    }

    [McpServerTool(Name = "get_graph_statistics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Return compact graph counts, confidence breakdowns, diagnostics, limitations, and suggested next tools without edge evidence. " + SeeSchemaNote)]
    public GraphStatisticsResponse GetGraphStatistics(
        [Description("Maximum returned diagnostic summaries. The server caps this to protect the agent context window.")] int? maxDiagnostics = null)
    {
        return _service.GetGraphStatistics(maxDiagnostics);
    }

    [McpServerTool(Name = "get_agent_summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Return compact graph orientation: central nodes, likely extension points, conservative clusters, limitations, and follow-up queries. " + SeeSchemaNote)]
    public AgentSummaryResponse GetAgentSummary(
        [Description("Approximate response budget: compact, standard, or detailed. This controls deterministic item caps, not exact token counts.")] string? budget = null,
        [Description("Maximum returned items per summary section. The server caps this to protect the agent context window.")] int? maxItemsPerSection = null)
    {
        return _service.GetAgentSummary(budget, maxItemsPerSection);
    }

    [McpServerTool(Name = "get_symbol_summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Return compact context for one symbol: relation counts, contained members, interface/DI links, and follow-up queries without bulk edge evidence. " + SeeSchemaNote)]
    public SymbolSummaryResponse GetSymbolSummary(
        [Description("Graph node id, label, or symbol.")] string idOrLabel,
        [Description("Maximum returned items per summary section. The server caps this to protect the agent context window.")] int? maxResults = null)
    {
        return _service.GetSymbolSummary(idOrLabel, maxResults);
    }

    [McpServerTool(Name = "plan_feature", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Rank likely existing edit points for adding a feature, especially when the new concept is absent from the graph. This is deterministic graph navigation, not an LLM-generated implementation plan. " + SeeSchemaNote)]
    public FeaturePlanResponse PlanFeature(
        [Description("Feature goal in natural language, such as 'add a new execution mode'.")] string goal,
        [Description("Optional seed node ids, labels, or symbols that should influence ranking.")] string[]? seedSymbols = null,
        [Description("Optional extra search terms when the goal uses domain vocabulary not yet represented in the graph.")] string[]? terms = null,
        [Description("Maximum returned ranked edit points. The server caps this to protect the agent context window.")] int? maxResults = null)
    {
        return _service.PlanFeature(goal, seedSymbols, terms, maxResults);
    }

    [McpServerTool(Name = "shortest_path", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Find the shortest directed path between two resolved graph nodes using existing graph edges only. " + SeeSchemaNote)]
    public PathResponse ShortestPath(
        [Description("Source node id, label, or symbol.")] string source,
        [Description("Target node id, label, or symbol.")] string target)
    {
        return _service.ShortestPath(source, target);
    }

    [McpServerTool(Name = "explain_path", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Find and explain a directed graph path with relation, confidence, and optional evidence. " + SeeSchemaNote)]
    public PathResponse ExplainPath(
        [Description("Source node id, label, or symbol.")] string source,
        [Description("Target node id, label, or symbol.")] string target,
        [Description("Whether to include evidence file, line, symbol, and reason for each edge.")] bool? includeEvidence = null)
    {
        return _service.ExplainPath(source, target, includeEvidence);
    }

    [McpServerTool(Name = "list_entrypoints", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List emitted application entrypoint nodes. If empty, current analyzers do not yet emit ASP.NET endpoint nodes. " + SeeSchemaNote)]
    public GraphSearchResponse ListEntrypoints([Description("Maximum returned entrypoint nodes.")] int? maxResults = null)
    {
        return _service.ListEntrypoints(maxResults);
    }

    [McpServerTool(Name = "find_flows_to_symbol", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reverse-traverse existing graph edges to find upstream nodes that can reach a target symbol. If no endpoint nodes exist, returns upstream nodes with an endpoint analyzer limitation. " + SeeSchemaNote)]
    public GraphSearchResponse FindFlowsToSymbol(
        [Description("Target node id, label, or symbol.")] string target,
        [Description("Reverse traversal depth. The server caps this to avoid huge traversals.")] int? maxDepth = null,
        [Description("Maximum returned upstream nodes and edges. The server caps this to protect the agent context window.")] int? maxResults = null,
        [Description("Include evidence file, line, symbol, and reason for returned edges. Defaults to false for compact bulk responses.")] bool? includeEvidence = null,
        [Description("Optional edge relations to exclude, such as contains, before traversal and result capping.")] string[]? excludeRelations = null)
    {
        return _service.FindFlowsToSymbolWithOptions(target, maxDepth, maxResults, includeEvidence, excludeRelations);
    }
}

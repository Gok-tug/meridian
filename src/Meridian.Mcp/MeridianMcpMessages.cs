namespace Meridian.Mcp;

public static class MeridianMcpMessages
{
    public const string StaleGraphNote = "This graph is precomputed. If source code changes, MCP results will not reflect those changes until meridian scan is run again.";

    public const string EndpointAnalyzerLimit = "Current Meridian analyzers do not yet emit ASP.NET Core endpoint nodes. Run this query again after endpoint analyzers are implemented and the graph is regenerated.";

    public static string TruncationNote(int limit)
    {
        return $"TRUNCATED: Limit of {limit} results reached. Use get_node or narrower query_graph filters to drill down.";
    }
}

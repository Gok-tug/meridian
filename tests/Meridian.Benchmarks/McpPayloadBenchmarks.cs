using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using Meridian.Abstractions;
using Meridian.Mcp;

namespace Meridian.Benchmarks;

[MemoryDiagnoser]
public class McpPayloadBenchmarks
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private MeridianGraphToolService service = null!;

    [GlobalSetup]
    public void Setup()
    {
        service = CreateService(GraphFixtureFactory.CreateBenchmarkPlanningGraph());
    }

    [Benchmark]
    public int AgentSummaryCompactPayloadBytes()
    {
        return SerializedByteCount(service.GetAgentSummary("compact", maxItemsPerSection: 3));
    }

    [Benchmark]
    public int SymbolSummaryPayloadBytes()
    {
        return SerializedByteCount(service.GetSymbolSummary("Component0", maxResults: 5));
    }

    [Benchmark]
    public int QueryGraphCompactPayloadBytes()
    {
        return SerializedByteCount(service.QueryGraphWithOptions(nodeKind: GraphNodeKinds.Method, relation: GraphRelations.Calls, maxResults: 10));
    }

    [Benchmark]
    public int QueryGraphEvidencePayloadBytes()
    {
        return SerializedByteCount(service.QueryGraphWithOptions(nodeKind: GraphNodeKinds.Method, relation: GraphRelations.Calls, includeEvidence: true, maxResults: 10));
    }

    [Benchmark]
    public int ShortestPathPayloadBytes()
    {
        return SerializedByteCount(service.ShortestPath("Component0.Operation0", "Component0.Operation7"));
    }

    [Benchmark]
    public int ExplainPathEvidencePayloadBytes()
    {
        return SerializedByteCount(service.ExplainPath("Component0.Operation0", "Component0.Operation7", includeEvidence: true));
    }

    internal static MeridianGraphToolService CreateService(GraphDocument graph)
    {
        return new MeridianGraphToolService(new McpGraphContext(graph, new MeridianMcpServerOptions { GraphPath = "benchmark.graph.json" }));
    }

    internal static int SerializedByteCount<T>(T payload)
    {
        return Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    internal static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

using BenchmarkDotNet.Attributes;
using Meridian.Abstractions;
using Meridian.Core;

namespace Meridian.Benchmarks;

[MemoryDiagnoser]
public class SummaryBenchmarks
{
    private GraphDocument graph = null!;
    private GraphSummaryService summaryService = null!;

    [Params(GraphSummaryBudget.Compact, GraphSummaryBudget.Standard, GraphSummaryBudget.Detailed)]
    public GraphSummaryBudget Budget { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        graph = GraphFixtureFactory.CreateBenchmarkPlanningGraph();
        summaryService = new GraphSummaryService();
    }

    [Benchmark]
    public AgentSummaryResult Summarize()
    {
        return summaryService.Summarize(graph, new GraphSummaryOptions { Budget = Budget });
    }
}

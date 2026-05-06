using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Meridian.Benchmarks;

internal static class BenchmarkConfig
{
    public static IConfig Create(bool quick)
    {
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .WithArtifactsPath(Path.Combine("artifacts", "benchmarks", "benchmarkdotnet"));
        if (quick)
        {
            config.AddJob(Job.ShortRun.WithId("quick"));
        }

        return config;
    }
}

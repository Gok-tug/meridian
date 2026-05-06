using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Exporters.Json;
using Meridian.Roslyn;

namespace Meridian.Cli;

internal static class ScanCommand
{
    private const string TrustProjectWarning = "WARNING: meridian scan uses MSBuild project evaluation. Scan only repositories you trust or run inside a sandbox. Pass --trust-project to suppress this warning.";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || CliHelp.IsHelp(args[0]))
        {
            CliHelp.PrintScan();
            return args.Length == 0 ? CliExitCodes.Usage : CliExitCodes.Success;
        }

        var inputPath = args[0];
        var outputDirectory = "meridian-out";
        var includeTests = false;
        var trustProject = false;
        var writeMetrics = false;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg == "--output" || arg == "-o") && i + 1 < args.Length)
            {
                outputDirectory = args[++i];
                continue;
            }

            if (arg == "--include-tests")
            {
                includeTests = true;
                continue;
            }

            if (arg == "--trust-project")
            {
                trustProject = true;
                continue;
            }

            if (arg == "--metrics")
            {
                writeMetrics = true;
                continue;
            }

            Console.Error.WriteLine($"Unknown or incomplete option: {arg}");
            return CliExitCodes.Usage;
        }

        try
        {
            var startedUtc = DateTimeOffset.UtcNow;
            var totalStopwatch = Stopwatch.StartNew();
            if (!trustProject)
            {
                Console.Error.WriteLine(TrustProjectWarning);
            }

            var analyzer = new RoslynFlowAnalyzer();
            var analyzeStopwatch = Stopwatch.StartNew();
            var graph = await analyzer.AnalyzeAsync(inputPath, new RoslynFlowAnalysisOptions
            {
                IncludeTests = includeTests,
                EmitMsBuildTrustBoundaryDiagnostic = !trustProject
            });
            analyzeStopwatch.Stop();

            var outputPath = Path.Combine(outputDirectory, "graph.json");
            var exportStopwatch = Stopwatch.StartNew();
            await JsonGraphExporter.WriteAsync(graph, outputPath);
            exportStopwatch.Stop();
            totalStopwatch.Stop();

            Console.WriteLine($"Meridian graph written to {outputPath}");
            Console.WriteLine($"Nodes: {graph.Nodes.Count}");
            Console.WriteLine($"Edges: {graph.Edges.Count}");
            Console.WriteLine($"Diagnostics: {graph.Diagnostics.Count}");
            if (writeMetrics)
            {
                var metricsPath = Path.Combine(outputDirectory, "metrics.json");
                var metrics = new ScanMetricsDocument(
                    MetricsVersion: "0.1",
                    Target: inputPath,
                    IncludeTests: includeTests,
                    TrustedProject: trustProject,
                    StartedUtc: startedUtc,
                    TotalMs: totalStopwatch.ElapsedMilliseconds,
                    AnalyzeMs: analyzeStopwatch.ElapsedMilliseconds,
                    ExportMs: exportStopwatch.ElapsedMilliseconds,
                    PeakWorkingSetMb: Math.Round(Process.GetCurrentProcess().PeakWorkingSet64 / 1024d / 1024d, 2),
                    NodeCount: graph.Nodes.Count,
                    EdgeCount: graph.Edges.Count,
                    DiagnosticCount: graph.Diagnostics.Count,
                    DotnetVersion: Environment.Version.ToString(),
                    OsDescription: RuntimeInformation.OSDescription,
                    MeridianVersion: graph.GeneratorVersion);
                await File.WriteAllTextAsync(metricsPath, JsonSerializer.Serialize(metrics, ScanMetricsJsonOptions.Instance));
                Console.WriteLine($"Metrics: {metricsPath}");
            }

            return CliExitCodes.Success;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Meridian scan failed: {exception.Message}");
            return CliExitCodes.Failure;
        }
    }

    private sealed record ScanMetricsDocument(
        [property: JsonPropertyName("metrics_version")] string MetricsVersion,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("include_tests")] bool IncludeTests,
        [property: JsonPropertyName("trusted_project")] bool TrustedProject,
        [property: JsonPropertyName("started_utc")] DateTimeOffset StartedUtc,
        [property: JsonPropertyName("total_ms")] long TotalMs,
        [property: JsonPropertyName("analyze_ms")] long AnalyzeMs,
        [property: JsonPropertyName("export_ms")] long ExportMs,
        [property: JsonPropertyName("peak_working_set_mb")] double PeakWorkingSetMb,
        [property: JsonPropertyName("node_count")] int NodeCount,
        [property: JsonPropertyName("edge_count")] int EdgeCount,
        [property: JsonPropertyName("diagnostic_count")] int DiagnosticCount,
        [property: JsonPropertyName("dotnet_version")] string DotnetVersion,
        [property: JsonPropertyName("os_description")] string OsDescription,
        [property: JsonPropertyName("meridian_version")] string MeridianVersion);

    private static class ScanMetricsJsonOptions
    {
        public static readonly JsonSerializerOptions Instance = new()
        {
            WriteIndented = true
        };
    }
}

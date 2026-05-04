using Meridian.Exporters.Json;
using Meridian.Roslyn;

namespace Meridian.Cli;

internal static class ScanCommand
{
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

            Console.Error.WriteLine($"Unknown or incomplete option: {arg}");
            return CliExitCodes.Usage;
        }

        try
        {
            var analyzer = new RoslynFlowAnalyzer();
            var graph = await analyzer.AnalyzeAsync(inputPath, new RoslynFlowAnalysisOptions { IncludeTests = includeTests });
            var outputPath = Path.Combine(outputDirectory, "graph.json");
            await JsonGraphExporter.WriteAsync(graph, outputPath);

            Console.WriteLine($"Meridian graph written to {outputPath}");
            Console.WriteLine($"Nodes: {graph.Nodes.Count}");
            Console.WriteLine($"Edges: {graph.Edges.Count}");
            Console.WriteLine($"Diagnostics: {graph.Diagnostics.Count}");
            return CliExitCodes.Success;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Meridian scan failed: {exception.Message}");
            return CliExitCodes.Failure;
        }
    }
}

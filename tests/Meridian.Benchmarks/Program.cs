using BenchmarkDotNet.Running;

namespace Meridian.Benchmarks;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsCommand(args[0], "benchmarks"))
        {
            return RunBenchmarks(args.Length == 0 ? [] : args[1..]);
        }

        if (IsCommand(args[0], "payload-report"))
        {
            var outputPath = GetOptionValue(args[1..], "--output") ?? Path.Combine("artifacts", "benchmarks", "mcp-payloads.json");
            await McpPayloadReport.WriteAsync(outputPath);
            Console.WriteLine($"Payload report: {outputPath}");
            return 0;
        }

        PrintUsage();
        return 1;
    }

    private static int RunBenchmarks(string[] args)
    {
        var quick = args.Any(arg => string.Equals(arg, "--quick", StringComparison.OrdinalIgnoreCase));
        var benchmarkArgs = args.Where(arg => !string.Equals(arg, "--quick", StringComparison.OrdinalIgnoreCase)).ToArray();
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(benchmarkArgs, BenchmarkConfig.Create(quick));
        return summaries.Any(summary => summary.HasCriticalValidationErrors || summary.Reports.Any(report => !report.Success)) ? 1 : 0;
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool IsCommand(string value, string command)
    {
        return string.Equals(value, command, StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Meridian.Benchmarks benchmarks [BenchmarkDotNet args] [--quick]");
        Console.WriteLine("  Meridian.Benchmarks payload-report [--output <path>]");
    }
}

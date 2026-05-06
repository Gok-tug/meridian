using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Cli.Rendering;
using Meridian.Core;
using Meridian.Exporters.Json;

namespace Meridian.Cli;

internal static class AgentSummaryCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && CliHelp.IsHelp(args[0]))
        {
            CliHelp.PrintAgentSummary();
            return CliExitCodes.Success;
        }

        var graphPath = "meridian-out/graph.json";
        var budget = GraphSummaryBudget.Standard;
        int? maxItems = null;
        var format = "text";
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg == "--graph" || arg == "-g") && i + 1 < args.Length)
            {
                graphPath = args[++i];
                continue;
            }

            if (arg == "--budget" && i + 1 < args.Length)
            {
                if (!GraphSummaryBudgetParser.TryParse(args[++i], out budget))
                {
                    Console.Error.WriteLine("Invalid budget. Expected compact, standard, or detailed.");
                    return CliExitCodes.Usage;
                }

                continue;
            }

            if (arg == "--max-items" && i + 1 < args.Length)
            {
                if (!int.TryParse(args[++i], out var parsedMaxItems) || parsedMaxItems <= 0)
                {
                    Console.Error.WriteLine("Invalid --max-items value. Expected a positive integer.");
                    return CliExitCodes.Usage;
                }

                maxItems = parsedMaxItems;
                continue;
            }

            if (arg == "--format" && i + 1 < args.Length)
            {
                format = args[++i].ToLowerInvariant();
                if (format is not "text" and not "json")
                {
                    Console.Error.WriteLine("Invalid format. Expected text or json.");
                    return CliExitCodes.Usage;
                }

                continue;
            }

            Console.Error.WriteLine($"Unknown or incomplete option: {arg}");
            return CliExitCodes.Usage;
        }

        try
        {
            var graph = await JsonGraphExporter.ReadAsync(graphPath);
            var summary = new GraphSummaryService().Summarize(graph, new GraphSummaryOptions
            {
                Budget = budget,
                MaxItemsPerSection = maxItems
            });

            if (format == "json")
            {
                Console.WriteLine(JsonSerializer.Serialize(summary, JsonOptions));
                return CliExitCodes.Success;
            }

            GraphConsoleRenderer.PrintAgentSummary(summary);
            return CliExitCodes.Success;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Meridian agent-summary failed: {exception.Message}");
            return CliExitCodes.Failure;
        }
    }
}

using Meridian.Cli.Rendering;
using Meridian.Core;
using Meridian.Exporters.Json;

namespace Meridian.Cli;

internal static class ExplainCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || CliHelp.IsHelp(args[0]))
        {
            CliHelp.PrintExplain();
            return args.Length == 0 ? CliExitCodes.Usage : CliExitCodes.Success;
        }

        var query = args[0];
        var graphPath = "meridian-out/graph.json";
        var parseResult = GraphOptionParser.Parse(args[1..], ref graphPath);
        if (parseResult != CliExitCodes.Success)
        {
            return parseResult;
        }

        try
        {
            var graph = await JsonGraphExporter.ReadAsync(graphPath);
            var queryService = new GraphQueryService(graph);
            var result = queryService.Explain(query);
            if (result is null)
            {
                Console.Error.WriteLine($"No node matched '{query}'.");
                return CliExitCodes.NotFound;
            }

            GraphConsoleRenderer.PrintExplain(result);
            return CliExitCodes.Success;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Meridian explain failed: {exception.Message}");
            return CliExitCodes.Failure;
        }
    }
}

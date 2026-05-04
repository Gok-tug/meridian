using Meridian.Cli.Rendering;
using Meridian.Core;
using Meridian.Exporters.Json;

namespace Meridian.Cli;

internal static class PathCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2 || CliHelp.IsHelp(args[0]))
        {
            CliHelp.PrintPath();
            return args.Length < 2 ? CliExitCodes.Usage : CliExitCodes.Success;
        }

        var sourceQuery = args[0];
        var targetQuery = args[1];
        var graphPath = "meridian-out/graph.json";
        var parseResult = GraphOptionParser.Parse(args[2..], ref graphPath);
        if (parseResult != CliExitCodes.Success)
        {
            return parseResult;
        }

        try
        {
            var graph = await JsonGraphExporter.ReadAsync(graphPath);
            var queryService = new GraphQueryService(graph);
            var result = queryService.FindPath(sourceQuery, targetQuery);
            if (result is null)
            {
                Console.Error.WriteLine($"No path found from '{sourceQuery}' to '{targetQuery}'.");
                return CliExitCodes.NotFound;
            }

            GraphConsoleRenderer.PrintPath(result);
            return CliExitCodes.Success;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Meridian path failed: {exception.Message}");
            return CliExitCodes.Failure;
        }
    }
}

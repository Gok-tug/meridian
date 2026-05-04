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
            var sourceResolution = queryService.ResolveNode(sourceQuery);
            if (PrintResolutionFailure("Source", sourceQuery, sourceResolution))
            {
                return CliExitCodes.NotFound;
            }

            var targetResolution = queryService.ResolveNode(targetQuery);
            if (PrintResolutionFailure("Target", targetQuery, targetResolution))
            {
                return CliExitCodes.NotFound;
            }

            var result = queryService.FindPath(sourceResolution.Node!, targetResolution.Node!);
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

    private static bool PrintResolutionFailure(string role, string query, GraphNodeResolution resolution)
    {
        if (resolution.Status == GraphNodeResolutionStatus.NotFound)
        {
            Console.Error.WriteLine($"No {role.ToLowerInvariant()} node matched '{query}'.");
            return true;
        }

        if (resolution.Status == GraphNodeResolutionStatus.Ambiguous)
        {
            Console.Error.WriteLine($"{role} node query '{query}' is ambiguous. Use a more precise label, symbol, or node ID.");
            GraphConsoleRenderer.PrintNodeCandidates(query, resolution.Candidates, Console.Error);
            return true;
        }

        return false;
    }
}

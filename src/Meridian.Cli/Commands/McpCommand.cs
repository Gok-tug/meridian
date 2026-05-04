using Meridian.Mcp;

namespace Meridian.Cli;

internal static class McpCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && CliHelp.IsHelp(args[0]))
        {
            CliHelp.PrintMcp();
            return CliExitCodes.Success;
        }

        var graphPath = "meridian-out/graph.json";
        var parseResult = GraphOptionParser.Parse(args, ref graphPath);
        if (parseResult != CliExitCodes.Success)
        {
            return parseResult;
        }

        if (!File.Exists(graphPath))
        {
            Console.Error.WriteLine($"Graph file not found: {graphPath}");
            return CliExitCodes.NotFound;
        }

        try
        {
            await MeridianMcpServer.RunAsync(new MeridianMcpServerOptions { GraphPath = graphPath });
            return CliExitCodes.Success;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Meridian MCP server failed: {exception.Message}");
            return CliExitCodes.Failure;
        }
    }
}

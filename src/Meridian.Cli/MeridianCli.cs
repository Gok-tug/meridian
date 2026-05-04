namespace Meridian.Cli;

internal static class MeridianCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || CliHelp.IsHelp(args[0]))
        {
            CliHelp.PrintRoot();
            return CliExitCodes.Success;
        }

        var command = args[0].ToLowerInvariant();
        return command switch
        {
            "scan" => await ScanCommand.RunAsync(args[1..]),
            "explain" => await ExplainCommand.RunAsync(args[1..]),
            "path" => await PathCommand.RunAsync(args[1..]),
            _ => UnknownCommand(command)
        };
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        CliHelp.PrintRoot();
        return CliExitCodes.Usage;
    }
}

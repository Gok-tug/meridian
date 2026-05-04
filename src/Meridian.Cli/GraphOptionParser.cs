namespace Meridian.Cli;

internal static class GraphOptionParser
{
    public static int Parse(string[] args, ref string graphPath)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg == "--graph" || arg == "-g") && i + 1 < args.Length)
            {
                graphPath = args[++i];
                continue;
            }

            Console.Error.WriteLine($"Unknown or incomplete option: {arg}");
            return CliExitCodes.Usage;
        }

        return CliExitCodes.Success;
    }
}

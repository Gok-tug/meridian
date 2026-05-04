namespace Meridian.Cli;

internal static class CliHelp
{
    public static bool IsHelp(string value)
    {
        return value is "-h" or "--help" or "help";
    }

    public static void PrintRoot()
    {
        Console.WriteLine("Meridian — semantic .NET application-flow graph generator");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  meridian scan <project-or-solution> [--output <directory>] [--include-tests]");
        Console.WriteLine("  meridian explain <node-or-symbol> [--graph <graph.json>]");
        Console.WriteLine("  meridian path <source> <target> [--graph <graph.json>]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  scan      Build meridian-out/graph.json from a .csproj, .sln, or .slnx file.");
        Console.WriteLine("  explain   Explain a graph node from graph.json.");
        Console.WriteLine("  path      Find a direct-call path between two graph nodes.");
    }

    public static void PrintScan()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  meridian scan <project-or-solution> [--output <directory>] [--include-tests]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  meridian scan samples/Sample.BasicCalls/Sample.BasicCalls.csproj");
        Console.WriteLine("  meridian scan MyApp.sln --output meridian-out");
    }

    public static void PrintExplain()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  meridian explain <node-or-symbol> [--graph <graph.json>]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  meridian explain \"OrderController.Get\" --graph meridian-out/graph.json");
    }

    public static void PrintPath()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  meridian path <source> <target> [--graph <graph.json>]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  meridian path \"OrderController.Get\" \"OrderService.Load\" --graph meridian-out/graph.json");
    }
}

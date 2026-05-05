using System.Diagnostics;
using Meridian.Exporters.Json;

namespace Meridian.Cli.Tests;

public sealed class MeridianCliProcessTests
{
    private static readonly TimeSpan CliTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Root_help_prints_usage()
    {
        var result = await RunCliAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.StandardOutput);
        Assert.Contains("meridian scan", result.StandardOutput);
    }

    [Fact]
    public async Task Unknown_command_returns_usage_error()
    {
        var result = await RunCliAsync("definitely-not-a-command");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Unknown command", result.StandardError);
        Assert.Contains("Usage:", result.StandardOutput);
    }

    [Fact]
    public async Task Scan_without_arguments_returns_usage_error()
    {
        var result = await RunCliAsync("scan");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage:", result.StandardOutput);
        Assert.Contains("meridian scan", result.StandardOutput);
    }

    [Fact]
    public async Task Scan_writes_parseable_non_empty_graph()
    {
        var outputDirectory = CreateTempDirectory();
        try
        {
            var result = await RunCliAsync("scan", SampleProjectPath(), "--output", outputDirectory, "--trust-project");
            var graphPath = Path.Combine(outputDirectory, "graph.json");

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(graphPath));
            var graph = JsonGraphExporter.Deserialize(await File.ReadAllTextAsync(graphPath));
            Assert.NotEmpty(graph.Nodes);
            Assert.NotEmpty(graph.Edges);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Scan_without_trust_project_prints_warning_and_records_diagnostic()
    {
        var outputDirectory = CreateTempDirectory();
        try
        {
            var result = await RunCliAsync("scan", SampleProjectPath(), "--output", outputDirectory);
            var graphPath = Path.Combine(outputDirectory, "graph.json");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("MSBuild project evaluation", result.StandardError);
            var graph = JsonGraphExporter.Deserialize(await File.ReadAllTextAsync(graphPath));
            Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Id == "MERIDIAN_MSBUILD_TRUST_BOUNDARY");
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Scan_with_trust_project_suppresses_warning_and_diagnostic()
    {
        var outputDirectory = CreateTempDirectory();
        try
        {
            var result = await RunCliAsync("scan", SampleProjectPath(), "--output", outputDirectory, "--trust-project");
            var graphPath = Path.Combine(outputDirectory, "graph.json");

            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain("MSBuild project evaluation", result.StandardError);
            var graph = JsonGraphExporter.Deserialize(await File.ReadAllTextAsync(graphPath));
            Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Id == "MERIDIAN_MSBUILD_TRUST_BOUNDARY");
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static async Task<CliResult> RunCliAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(CliAssemblyPath());
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("CLI process could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(CliTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw new TimeoutException($"CLI process timed out after {CliTimeout}. Args: {string.Join(' ', args)}. Stdout: {await stdout}. Stderr: {await stderr}");
        }

        return new CliResult(process.ExitCode, await stdout, await stderr);
    }

    private static string CliAssemblyPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Meridian.Cli.dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Meridian.Cli.dll was not copied to the test output directory.", path);
        }

        return path;
    }

    private static string SampleProjectPath()
    {
        return Path.Combine(RepositoryRoot(), "samples", "Sample.BasicCalls", "Sample.BasicCalls.csproj");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be found.");
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Meridian.Cli.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);
}

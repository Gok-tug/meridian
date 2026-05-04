using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Meridian.Roslyn;

internal static class RoslynProjectLoader
{
    public static async Task<IReadOnlyList<Project>> LoadProjectsAsync(
        MSBuildWorkspace workspace,
        string fullPath,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fullPath);
        if (IsSolutionPath(fullPath))
        {
            var solution = await workspace.OpenSolutionAsync(fullPath, cancellationToken: cancellationToken);
            return solution.Projects.ToArray();
        }

        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var project = await workspace.OpenProjectAsync(fullPath, cancellationToken: cancellationToken);
            return [project];
        }

        throw new NotSupportedException($"Unsupported input file '{fullPath}'. Expected .csproj, .sln, or .slnx.");
    }

    public static IEnumerable<Project> SelectProjects(
        IEnumerable<Project> projects,
        string fullPath,
        RoslynFlowAnalysisOptions options)
    {
        var shouldFilterTests = IsSolutionPath(fullPath) && !options.IncludeTests;
        return projects
            .Where(project => !shouldFilterTests || !IsLikelyTestProject(project))
            .OrderBy(project => project.FilePath, StringComparer.Ordinal);
    }

    private static bool IsSolutionPath(string fullPath)
    {
        var extension = Path.GetExtension(fullPath);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyTestProject(Project project)
    {
        if (project.Name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
            project.Name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (project.FilePath is null || !File.Exists(project.FilePath))
        {
            return false;
        }

        var projectFile = File.ReadAllText(project.FilePath);
        return projectFile.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase) ||
            projectFile.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase);
    }
}

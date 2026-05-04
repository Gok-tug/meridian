using Meridian.Abstractions;
using Meridian.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace Meridian.Roslyn;

public sealed class RoslynFlowAnalyzer
{
    public async Task<GraphDocument> AnalyzeAsync(
        string projectOrSolutionPath,
        RoslynFlowAnalysisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectOrSolutionPath);
        options ??= new RoslynFlowAnalysisOptions();

        var fullPath = Path.GetFullPath(projectOrSolutionPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Project or solution file was not found.", fullPath);
        }

        var rootDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        var graph = new GraphBuilder();

        MSBuildRegistration.EnsureRegistered();

        using var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            graph.AddDiagnostic(new GraphDiagnostic
            {
                Id = "MERIDIAN_WORKSPACE",
                Severity = args.Diagnostic.Kind.ToString().ToLowerInvariant(),
                Message = args.Diagnostic.Message
            });
        });

        var projects = await LoadProjectsAsync(workspace, fullPath, cancellationToken);
        var shouldFilterTests = IsSolutionPath(fullPath) && !options.IncludeTests;
        foreach (var project in projects
            .Where(project => !shouldFilterTests || !IsLikelyTestProject(project))
            .OrderBy(project => project.FilePath, StringComparer.Ordinal))
        {
            await AnalyzeProjectAsync(project, graph, rootDirectory, cancellationToken);
        }

        return graph.Build(".");
    }

    private static async Task<IReadOnlyList<Project>> LoadProjectsAsync(
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

    private static async Task AnalyzeProjectAsync(
        Project project,
        GraphBuilder graph,
        string rootDirectory,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation is null)
        {
            graph.AddDiagnostic(new GraphDiagnostic
            {
                Id = "MERIDIAN_COMPILATION",
                Severity = "warning",
                Message = $"Compilation could not be created for project '{project.Name}'."
            });
            return;
        }

        foreach (var document in project.Documents.OrderBy(document => document.FilePath, StringComparer.Ordinal))
        {
            await AnalyzeDocumentAsync(document, graph, rootDirectory, cancellationToken);
        }
    }

    private static async Task AnalyzeDocumentAsync(
        Document document,
        GraphBuilder graph,
        string rootDirectory,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (root is null || semanticModel is null)
        {
            return;
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceSymbol = semanticModel.GetEnclosingSymbol(invocation.SpanStart, cancellationToken) as IMethodSymbol;
            var targetSymbol = ResolveTargetMethod(semanticModel, invocation, cancellationToken);
            if (sourceSymbol is null || targetSymbol is null)
            {
                continue;
            }

            if (!HasSourceLocation(sourceSymbol) || !HasSourceLocation(targetSymbol))
            {
                continue;
            }

            var sourceNode = CreateMethodNode(sourceSymbol, rootDirectory);
            var targetNode = CreateMethodNode(targetSymbol, rootDirectory);
            graph.AddNode(sourceNode);
            graph.AddNode(targetNode);

            var invocationLocation = invocation.GetLocation();
            graph.AddEdge(new GraphEdge
            {
                Source = sourceNode.Id,
                Target = targetNode.Id,
                Relation = GraphRelations.Calls,
                Confidence = ConfidenceLevels.Extracted,
                ConfidenceScore = 1.0,
                Evidence = new GraphEvidence
                {
                    File = SourcePath.RelativeFile(invocationLocation, rootDirectory),
                    Line = SourcePath.Line(invocationLocation),
                    Symbol = sourceNode.Symbol,
                    Reason = $"Roslyn resolved invocation '{invocation.Expression}' to '{targetNode.Symbol}'."
                }
            });
        }
    }

    private static IMethodSymbol? ResolveTargetMethod(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
        {
            return methodSymbol.ReducedFrom ?? methodSymbol;
        }

        if (symbolInfo.CandidateSymbols.Length == 1 && symbolInfo.CandidateSymbols[0] is IMethodSymbol candidate)
        {
            return candidate.ReducedFrom ?? candidate;
        }

        return null;
    }

    private static GraphNode CreateMethodNode(IMethodSymbol methodSymbol, string rootDirectory)
    {
        var location = methodSymbol.Locations.First(static location => location.IsInSource);
        var symbol = methodSymbol.ToDisplayString(SymbolDisplay.MethodFormat);
        return new GraphNode
        {
            Id = $"method:{methodSymbol.ContainingAssembly.Identity.Name}:{symbol}",
            Label = $"{methodSymbol.ContainingType.Name}.{methodSymbol.Name}",
            Kind = GraphNodeKinds.Method,
            Symbol = symbol,
            SourceFile = SourcePath.RelativeFile(location, rootDirectory),
            SourceLocation = SourcePath.SourceLocation(location)
        };
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

    private static bool HasSourceLocation(ISymbol symbol)
    {
        return symbol.Locations.Any(static location => location.IsInSource);
    }
}

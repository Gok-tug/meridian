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
        if (options.EmitMsBuildTrustBoundaryDiagnostic)
        {
            graph.AddDiagnostic(new GraphDiagnostic
            {
                Id = "MERIDIAN_MSBUILD_TRUST_BOUNDARY",
                Severity = "warning",
                Message = "Meridian scan uses MSBuildWorkspace to evaluate project and solution files. Scan only repositories you trust or run Meridian inside a sandbox."
            });
        }

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

        var sourceFilter = new RoslynSourceFilter(rootDirectory);
        var mediatrClassifier = new MediatRSymbolClassifier();
        var efCoreClassifier = new EfCoreSymbolClassifier();
        var graphFactory = new RoslynGraphFactory(rootDirectory, sourceFilter, typeSymbol =>
        {
            var efCoreKind = efCoreClassifier.ClassifyType(typeSymbol);
            return efCoreKind == GraphNodeKinds.Type ? mediatrClassifier.ClassifyType(typeSymbol) : efCoreKind;
        });
        var typeDeclarationAnalyzer = new TypeDeclarationAnalyzer(sourceFilter, graphFactory);
        var directCallAnalyzer = new DirectCallAnalyzer(sourceFilter, graphFactory);
        var dependencyInjectionAnalyzer = new DependencyInjectionAnalyzer(sourceFilter, graphFactory);
        var mediatrDeclarationAnalyzer = new MediatRDeclarationAnalyzer(sourceFilter, graphFactory, mediatrClassifier);
        var mediatrCallSiteAnalyzer = new MediatRCallSiteAnalyzer(sourceFilter, graphFactory, mediatrClassifier);
        var efCoreAnalyzer = new EfCoreAnalyzer(sourceFilter, graphFactory, efCoreClassifier);
        var reflectionAnalyzer = new ReflectionAnalyzer(sourceFilter, graphFactory);

        var projects = await RoslynProjectLoader.LoadProjectsAsync(workspace, fullPath, cancellationToken);
        foreach (var project in RoslynProjectLoader.SelectProjects(projects, fullPath, options))
        {
            await AnalyzeProjectAsync(
                project,
                graph,
                sourceFilter,
                typeDeclarationAnalyzer,
                directCallAnalyzer,
                dependencyInjectionAnalyzer,
                mediatrDeclarationAnalyzer,
                mediatrCallSiteAnalyzer,
                efCoreAnalyzer,
                reflectionAnalyzer,
                cancellationToken);
        }

        return graph.Build(".");
    }

    private static async Task AnalyzeProjectAsync(
        Project project,
        GraphBuilder graph,
        RoslynSourceFilter sourceFilter,
        TypeDeclarationAnalyzer typeDeclarationAnalyzer,
        DirectCallAnalyzer directCallAnalyzer,
        DependencyInjectionAnalyzer dependencyInjectionAnalyzer,
        MediatRDeclarationAnalyzer mediatrDeclarationAnalyzer,
        MediatRCallSiteAnalyzer mediatrCallSiteAnalyzer,
        EfCoreAnalyzer efCoreAnalyzer,
        ReflectionAnalyzer reflectionAnalyzer,
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

        foreach (var document in project.Documents
            .Where(sourceFilter.IsAnalyzableDocument)
            .OrderBy(document => document.FilePath, StringComparer.Ordinal))
        {
            await AnalyzeDocumentAsync(
                document,
                graph,
                typeDeclarationAnalyzer,
                directCallAnalyzer,
                dependencyInjectionAnalyzer,
                mediatrDeclarationAnalyzer,
                mediatrCallSiteAnalyzer,
                efCoreAnalyzer,
                reflectionAnalyzer,
                cancellationToken);
        }
    }

    private static async Task AnalyzeDocumentAsync(
        Document document,
        GraphBuilder graph,
        TypeDeclarationAnalyzer typeDeclarationAnalyzer,
        DirectCallAnalyzer directCallAnalyzer,
        DependencyInjectionAnalyzer dependencyInjectionAnalyzer,
        MediatRDeclarationAnalyzer mediatrDeclarationAnalyzer,
        MediatRCallSiteAnalyzer mediatrCallSiteAnalyzer,
        EfCoreAnalyzer efCoreAnalyzer,
        ReflectionAnalyzer reflectionAnalyzer,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (root is null || semanticModel is null)
        {
            return;
        }

        foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>().OrderBy(type => type.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var typeResult = typeDeclarationAnalyzer.Analyze(typeDeclaration, semanticModel, graph, cancellationToken);
            if (typeResult is { } result)
            {
                dependencyInjectionAnalyzer.AnalyzeConstructorInjection(result, graph);
                mediatrDeclarationAnalyzer.Analyze(result, semanticModel, graph, cancellationToken);
                efCoreAnalyzer.AnalyzeType(result, graph);
            }
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>().OrderBy(invocation => invocation.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            directCallAnalyzer.Analyze(invocation, semanticModel, graph, cancellationToken);
            dependencyInjectionAnalyzer.AnalyzeRegistration(invocation, semanticModel, graph, cancellationToken);
            mediatrCallSiteAnalyzer.Analyze(invocation, semanticModel, graph, cancellationToken);
            efCoreAnalyzer.AnalyzeInvocation(invocation, semanticModel, graph, cancellationToken);
            reflectionAnalyzer.AnalyzeInvocation(invocation, semanticModel, graph, cancellationToken);
        }

        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().OrderBy(memberAccess => memberAccess.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            efCoreAnalyzer.AnalyzeMemberAccess(memberAccess, semanticModel, graph, cancellationToken);
        }

        foreach (var conditionalAccess in root.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>().OrderBy(conditionalAccess => conditionalAccess.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            efCoreAnalyzer.AnalyzeConditionalAccess(conditionalAccess, semanticModel, graph, cancellationToken);
        }

        foreach (var typeOfExpression in root.DescendantNodes().OfType<TypeOfExpressionSyntax>().OrderBy(typeOfExpression => typeOfExpression.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            reflectionAnalyzer.AnalyzeTypeOf(typeOfExpression, semanticModel, graph, cancellationToken);
        }
    }
}

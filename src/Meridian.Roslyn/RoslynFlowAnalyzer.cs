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
                Severity = WorkspaceDiagnosticSeverityMapper.Map(args.Diagnostic),
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
        var analyzers = new RoslynAnalyzerSet(
            new TypeDeclarationAnalyzer(sourceFilter, graphFactory),
            new MemberReferenceAnalyzer(sourceFilter, graphFactory),
            new DirectCallAnalyzer(sourceFilter, graphFactory),
            new DependencyInjectionAnalyzer(sourceFilter, graphFactory),
            new MediatRDeclarationAnalyzer(sourceFilter, graphFactory, mediatrClassifier),
            new MediatRCallSiteAnalyzer(sourceFilter, graphFactory, mediatrClassifier),
            new CommunityToolkitMvvmAnalyzer(sourceFilter, graphFactory),
            new ConditionalFlowAnalyzer(sourceFilter, graphFactory),
            new AvaloniaAxamlBindingAnalyzer(sourceFilter, graphFactory),
            new EfCoreAnalyzer(sourceFilter, graphFactory, efCoreClassifier),
            new ReflectionAnalyzer(sourceFilter, graphFactory),
            new AspNetCoreEndpointAnalyzer(sourceFilter, graphFactory, mediatrClassifier));

        var projects = await RoslynProjectLoader.LoadProjectsAsync(workspace, fullPath, cancellationToken);
        foreach (var project in RoslynProjectLoader.SelectProjects(projects, fullPath, options))
        {
            await AnalyzeProjectAsync(
                project,
                graph,
                sourceFilter,
                analyzers,
                cancellationToken);
        }

        return graph.Build(".");
    }

    private static async Task AnalyzeProjectAsync(
        Project project,
        GraphBuilder graph,
        RoslynSourceFilter sourceFilter,
        RoslynAnalyzerSet analyzers,
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
                analyzers,
                cancellationToken);
        }

        analyzers.AvaloniaAxamlBindingAnalyzer.AnalyzeProject(project, compilation, graph, cancellationToken);
    }

    private static async Task AnalyzeDocumentAsync(
        Document document,
        GraphBuilder graph,
        RoslynAnalyzerSet analyzers,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (root is null || semanticModel is null)
        {
            return;
        }

        var endpointContext = analyzers.AspNetCoreEndpointAnalyzer.CreateDocumentContext(root, semanticModel, cancellationToken);

        foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>().OrderBy(type => type.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var typeResult = analyzers.TypeDeclarationAnalyzer.Analyze(typeDeclaration, semanticModel, graph, cancellationToken);
            if (typeResult is { } result)
            {
                analyzers.DependencyInjectionAnalyzer.AnalyzeConstructorInjection(result, graph);
                analyzers.MediatRDeclarationAnalyzer.Analyze(result, semanticModel, graph, cancellationToken);
                analyzers.CommunityToolkitMvvmAnalyzer.Analyze(result, graph);
                analyzers.EfCoreAnalyzer.AnalyzeType(result, graph);
                analyzers.AspNetCoreEndpointAnalyzer.AnalyzeType(typeDeclaration, result, semanticModel, graph, cancellationToken);
            }
        }

        foreach (var enumDeclaration in root.DescendantNodes().OfType<EnumDeclarationSyntax>().OrderBy(enumDeclaration => enumDeclaration.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            analyzers.TypeDeclarationAnalyzer.AnalyzeEnum(enumDeclaration, semanticModel, graph, cancellationToken);
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>().OrderBy(invocation => invocation.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            analyzers.DirectCallAnalyzer.Analyze(invocation, semanticModel, graph, cancellationToken);
            analyzers.DependencyInjectionAnalyzer.AnalyzeRegistration(invocation, semanticModel, graph, cancellationToken);
            analyzers.MediatRCallSiteAnalyzer.Analyze(invocation, semanticModel, graph, cancellationToken);
            analyzers.EfCoreAnalyzer.AnalyzeInvocation(invocation, semanticModel, graph, cancellationToken);
            analyzers.ReflectionAnalyzer.AnalyzeInvocation(invocation, semanticModel, graph, cancellationToken);
            analyzers.AspNetCoreEndpointAnalyzer.AnalyzeInvocation(invocation, semanticModel, endpointContext, graph, cancellationToken);
        }

        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().OrderBy(memberAccess => memberAccess.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            analyzers.EfCoreAnalyzer.AnalyzeMemberAccess(memberAccess, semanticModel, graph, cancellationToken);
        }

        foreach (var conditionalAccess in root.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>().OrderBy(conditionalAccess => conditionalAccess.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            analyzers.EfCoreAnalyzer.AnalyzeConditionalAccess(conditionalAccess, semanticModel, graph, cancellationToken);
        }

        foreach (var typeOfExpression in root.DescendantNodes().OfType<TypeOfExpressionSyntax>().OrderBy(typeOfExpression => typeOfExpression.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            analyzers.ReflectionAnalyzer.AnalyzeTypeOf(typeOfExpression, semanticModel, graph, cancellationToken);
        }

        foreach (var ifStatement in root.DescendantNodes().OfType<IfStatementSyntax>().OrderBy(ifStatement => ifStatement.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            analyzers.ConditionalFlowAnalyzer.AnalyzeIf(ifStatement, semanticModel, graph, cancellationToken);
        }

        foreach (var switchStatement in root.DescendantNodes().OfType<SwitchStatementSyntax>().OrderBy(switchStatement => switchStatement.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            analyzers.ConditionalFlowAnalyzer.AnalyzeSwitch(switchStatement, semanticModel, graph, cancellationToken);
        }

        foreach (var referenceNode in root.DescendantNodes()
            .Where(MemberReferenceAnalyzer.IsCandidateReference)
            .OrderBy(referenceNode => referenceNode.SpanStart)
            .ThenBy(referenceNode => referenceNode.Span.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            analyzers.MemberReferenceAnalyzer.Analyze(referenceNode, semanticModel, graph, cancellationToken);
        }
    }
}

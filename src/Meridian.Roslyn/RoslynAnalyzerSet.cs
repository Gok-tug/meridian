namespace Meridian.Roslyn;

internal sealed record RoslynAnalyzerSet(
    TypeDeclarationAnalyzer TypeDeclarationAnalyzer,
    MemberReferenceAnalyzer MemberReferenceAnalyzer,
    DirectCallAnalyzer DirectCallAnalyzer,
    DependencyInjectionAnalyzer DependencyInjectionAnalyzer,
    MediatRDeclarationAnalyzer MediatRDeclarationAnalyzer,
    MediatRCallSiteAnalyzer MediatRCallSiteAnalyzer,
    EfCoreAnalyzer EfCoreAnalyzer,
    ReflectionAnalyzer ReflectionAnalyzer,
    AspNetCoreEndpointAnalyzer AspNetCoreEndpointAnalyzer);

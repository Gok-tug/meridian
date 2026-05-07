namespace Meridian.Roslyn;

internal sealed record RoslynAnalyzerSet(
    TypeDeclarationAnalyzer TypeDeclarationAnalyzer,
    MemberReferenceAnalyzer MemberReferenceAnalyzer,
    DirectCallAnalyzer DirectCallAnalyzer,
    DependencyInjectionAnalyzer DependencyInjectionAnalyzer,
    MediatRDeclarationAnalyzer MediatRDeclarationAnalyzer,
    MediatRCallSiteAnalyzer MediatRCallSiteAnalyzer,
    CommunityToolkitMvvmAnalyzer CommunityToolkitMvvmAnalyzer,
    ConditionalFlowAnalyzer ConditionalFlowAnalyzer,
    AvaloniaAxamlBindingAnalyzer AvaloniaAxamlBindingAnalyzer,
    EfCoreAnalyzer EfCoreAnalyzer,
    ReflectionAnalyzer ReflectionAnalyzer,
    AspNetCoreEndpointAnalyzer AspNetCoreEndpointAnalyzer);

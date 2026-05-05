namespace Meridian.Roslyn;

public sealed record RoslynFlowAnalysisOptions
{
    public bool IncludeTests { get; init; }

    public bool EmitMsBuildTrustBoundaryDiagnostic { get; init; }
}

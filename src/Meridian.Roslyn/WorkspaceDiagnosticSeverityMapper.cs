using Microsoft.CodeAnalysis;

namespace Meridian.Roslyn;

internal static class WorkspaceDiagnosticSeverityMapper
{
    public static string Map(WorkspaceDiagnostic diagnostic)
    {
        return diagnostic.Kind switch
        {
            WorkspaceDiagnosticKind.Warning => "warning",
            WorkspaceDiagnosticKind.Failure when IsKnownNonFatalWarning(diagnostic.Message) => "warning",
            WorkspaceDiagnosticKind.Failure => "error",
            _ => "warning"
        };
    }

    private static bool IsKnownNonFatalWarning(string message)
    {
        if (ContainsAny(message, "error NU", "error MSB", "error CS", "hata NU", "hata MSB", "hata CS"))
        {
            return false;
        }

        return ContainsAny(message, "warning NU", "warning MSB", "warning CS", "uyarı NU", "uyarı MSB", "uyarı CS") ||
            ContainsAny(
                message,
                "known moderate severity vulnerability",
                "known high severity vulnerability",
                "known critical severity vulnerability",
                "güvenlik açığı",
                "github.com/advisories/",
                "NuGetAudit");
    }

    private static bool ContainsAny(string value, params string[] fragments)
    {
        return fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}

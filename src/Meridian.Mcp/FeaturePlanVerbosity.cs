namespace Meridian.Mcp;

public enum FeaturePlanVerbosity
{
    Compact,
    Standard,
    Detailed
}

internal static class FeaturePlanVerbosityExtensions
{
    public static string ToWireValue(this FeaturePlanVerbosity verbosity)
    {
        return verbosity switch
        {
            FeaturePlanVerbosity.Compact => "compact",
            FeaturePlanVerbosity.Detailed => "detailed",
            _ => "standard"
        };
    }
}

internal static class FeaturePlanVerbosityParser
{
    public static bool TryParse(string? value, out FeaturePlanVerbosity verbosity)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            verbosity = FeaturePlanVerbosity.Standard;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "compact":
                verbosity = FeaturePlanVerbosity.Compact;
                return true;
            case "standard":
                verbosity = FeaturePlanVerbosity.Standard;
                return true;
            case "detailed":
                verbosity = FeaturePlanVerbosity.Detailed;
                return true;
            default:
                verbosity = FeaturePlanVerbosity.Standard;
                return false;
        }
    }
}

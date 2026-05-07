namespace Sample.ConditionalFlow;

public enum RoutingMode
{
    Standard,
    Fast,
    Offline
}

public sealed class RoutingRequest
{
    public const string CanaryRegion = "canary";

    public RoutingMode Mode { get; set; } = RoutingMode.Standard;

    public string Region { get; set; } = "local";
}

public sealed class RoutingDecider
{
    private const string EmergencyRegion = "emergency";

    public string Decide(RoutingRequest request)
    {
        if (request.Mode == RoutingMode.Fast)
        {
            return "fast";
        }

        if (request.Region == RoutingRequest.CanaryRegion)
        {
            return "canary";
        }

        if (DateTime.UtcNow.Day < 0)
        {
            return EmergencyRegion;
        }

        switch (request.Mode)
        {
            case RoutingMode.Offline:
                return "offline";
            case RoutingMode.Standard:
                return "standard";
            default:
                return EmergencyRegion;
        }
    }

    public string DecideByMode(RoutingMode mode)
    {
        switch (mode)
        {
            case RoutingMode.Fast:
                return "fast";
            default:
                return "other";
        }
    }
}

using Microsoft.Build.Locator;

namespace Meridian.Roslyn;

internal static class MSBuildRegistration
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered || MSBuildLocator.IsRegistered)
        {
            return;
        }

        lock (Gate)
        {
            if (_registered || MSBuildLocator.IsRegistered)
            {
                _registered = true;
                return;
            }

            MSBuildLocator.RegisterDefaults();
            _registered = true;
        }
    }
}

using Microsoft.CodeAnalysis;

namespace Meridian.Roslyn;

internal static class SourcePath
{
    public static string? RelativeFile(Location location, string rootDirectory)
    {
        if (!location.IsInSource || location.SourceTree?.FilePath is not { Length: > 0 } filePath)
        {
            return null;
        }

        return RelativeFile(filePath, rootDirectory);
    }

    public static string RelativeFile(string filePath, string rootDirectory)
    {
        return Normalize(Path.GetRelativePath(rootDirectory, filePath));
    }

    public static int? Line(Location location)
    {
        if (!location.IsInSource)
        {
            return null;
        }

        return location.GetLineSpan().StartLinePosition.Line + 1;
    }

    public static string? SourceLocation(Location location)
    {
        var line = Line(location);
        return line is null ? null : $"L{line}";
    }

    public static string Normalize(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}

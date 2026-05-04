using Microsoft.CodeAnalysis;

namespace Meridian.Roslyn;

internal sealed class RoslynSourceFilter
{
    private readonly string _rootDirectory;

    public RoslynSourceFilter(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = rootDirectory;
    }

    public bool IsAnalyzableDocument(Document document)
    {
        return IsAnalyzablePath(document.FilePath);
    }

    public bool HasAnalyzableSourceLocation(ISymbol symbol)
    {
        return symbol.Locations.Any(IsAnalyzableSourceLocation);
    }

    public Location? TryFirstAnalyzableSourceLocation(ISymbol symbol)
    {
        return symbol.Locations
            .Where(IsAnalyzableSourceLocation)
            .OrderBy(LocationSortKey, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public Location FirstAnalyzableSourceLocation(ISymbol symbol)
    {
        return TryFirstAnalyzableSourceLocation(symbol) ??
            throw new InvalidOperationException($"No analyzable source location found for symbol '{symbol.ToDisplayString()}'.");
    }

    public Location FirstAnalyzableSourceLocation(IParameterSymbol parameter, Location fallback)
    {
        return parameter.Locations
            .Where(IsAnalyzableSourceLocation)
            .OrderBy(LocationSortKey, StringComparer.Ordinal)
            .FirstOrDefault() ?? fallback;
    }

    private bool IsAnalyzableSourceLocation(Location location)
    {
        return location.IsInSource && IsAnalyzablePath(location.SourceTree?.FilePath);
    }

    private bool IsAnalyzablePath(string? filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) && !IsGeneratedPath(filePath);
    }

    private bool IsGeneratedPath(string filePath)
    {
        var relativePath = SourcePath.Normalize(Path.GetRelativePath(_rootDirectory, filePath));
        var fileName = Path.GetFileName(filePath);
        return ContainsPathSegment(relativePath, "obj") ||
            ContainsPathSegment(relativePath, "bin") ||
            fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    private string LocationSortKey(Location location)
    {
        var filePath = location.SourceTree?.FilePath ?? string.Empty;
        var lineSpan = location.GetLineSpan();
        return string.Join(
            '',
            SourcePath.Normalize(Path.GetRelativePath(_rootDirectory, filePath)),
            lineSpan.StartLinePosition.Line.ToString("D10"),
            lineSpan.StartLinePosition.Character.ToString("D10"));
    }

    private static bool ContainsPathSegment(string path, string segment)
    {
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.Equals(segment, StringComparison.OrdinalIgnoreCase));
    }
}

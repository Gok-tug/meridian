using Meridian.Mcp.Responses;

namespace Meridian.Mcp;

/// <summary>
/// Surfaces filename-based documentation hints by globbing well-known repository locations
/// (CONTRIBUTING.md, AGENTS.md, README.md, docs/, .agent/, .cursor/rules/) and scoring matches by filename
/// overlap with the goal terms. The provider never reads file contents; it only inspects file names and
/// modification timestamps so that stale documents can be flagged.
/// </summary>
public sealed class FilesystemDocHintProvider : IDocHintProvider
{
    private static readonly string[] CandidateDirectories =
    [
        "docs",
        ".agent",
        ".cursor/rules",
        ".github"
    ];

    private static readonly string[] CandidateRootFiles =
    [
        "AGENTS.md",
        "CONTRIBUTING.md",
        "ARCHITECTURE.md",
        "ROADMAP.md",
        "CLAUDE.md",
        "README.md"
    ];

    private const int StaleThresholdDays = 180;

    private readonly string _rootDirectory;
    private readonly Func<DateTimeOffset> _now;

    public FilesystemDocHintProvider(string rootDirectory)
        : this(rootDirectory, () => DateTimeOffset.UtcNow)
    {
    }

    internal FilesystemDocHintProvider(string rootDirectory, Func<DateTimeOffset> now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(now);
        _rootDirectory = rootDirectory;
        _now = now;
    }

    public IReadOnlyList<DocHintDto> GetHints(string goal, IReadOnlyList<string> terms, int maxHints)
    {
        if (maxHints <= 0)
        {
            return [];
        }

        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        var lowerTerms = terms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (lowerTerms.Length == 0)
        {
            return [];
        }

        var candidates = EnumerateCandidateFiles().ToArray();
        var now = _now();
        var hints = new List<DocHintDto>();
        foreach (var path in candidates)
        {
            var relative = MakeRelative(path);
            var score = ScoreFileName(relative, lowerTerms);
            if (score <= 0)
            {
                continue;
            }

            DateTimeOffset? lastModified = null;
            int? ageDays = null;
            try
            {
                var info = new FileInfo(path);
                if (info.Exists)
                {
                    var modifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                    lastModified = modifiedUtc;
                    ageDays = (int)Math.Max(0, (now - modifiedUtc).TotalDays);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            var stale = ageDays is > StaleThresholdDays;
            var reason = stale
                ? $"Filename overlaps goal terms; last modified {ageDays} day(s) ago — likely stale, verify before relying."
                : "Filename overlaps goal terms.";
            hints.Add(new DocHintDto(relative, reason, Math.Round(score, 3), lastModified, ageDays));
        }

        return hints
            .OrderByDescending(hint => hint.MatchScore)
            .ThenBy(hint => hint.Path, StringComparer.Ordinal)
            .Take(maxHints)
            .ToArray();
    }

    private IEnumerable<string> EnumerateCandidateFiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootFile in CandidateRootFiles)
        {
            var path = Path.Combine(_rootDirectory, rootFile);
            if (File.Exists(path) && seen.Add(path))
            {
                yield return path;
            }
        }

        foreach (var directory in CandidateDirectories)
        {
            var directoryPath = Path.Combine(_rootDirectory, directory);
            if (!Directory.Exists(directoryPath))
            {
                continue;
            }

            IEnumerable<string> markdownFiles;
            try
            {
                markdownFiles = Directory.EnumerateFiles(directoryPath, "*.md", SearchOption.AllDirectories);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in markdownFiles)
            {
                if (seen.Add(file))
                {
                    yield return file;
                }
            }
        }
    }

    private string MakeRelative(string fullPath)
    {
        var relative = Path.GetRelativePath(_rootDirectory, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static double ScoreFileName(string relativePath, IReadOnlyList<string> lowerTerms)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath).ToLowerInvariant();
        if (string.IsNullOrEmpty(fileName))
        {
            return 0;
        }

        var directorySegments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .SkipLast(1)
            .Select(segment => segment.ToLowerInvariant())
            .ToArray();

        double score = 0;
        foreach (var term in lowerTerms)
        {
            if (fileName == term)
            {
                score += 1.0;
                continue;
            }

            if (fileName.Contains(term, StringComparison.Ordinal))
            {
                score += 0.7;
                continue;
            }

            if (directorySegments.Any(segment => segment.Contains(term, StringComparison.Ordinal)))
            {
                score += 0.3;
            }
        }

        if (lowerTerms.Count == 0)
        {
            return 0;
        }

        return Math.Min(score / lowerTerms.Count, 1.0);
    }
}

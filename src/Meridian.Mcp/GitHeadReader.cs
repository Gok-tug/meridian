namespace Meridian.Mcp;

/// <summary>
/// Reads the current HEAD commit SHA from a working tree's .git directory without invoking git.
/// Returns null when the directory is not a git working tree, when reading fails, or when the
/// .git layout is unsupported (for example, partial submodules without a HEAD file). This is used
/// to compare graph provenance against the live repository state at MCP load time.
/// </summary>
internal static class GitHeadReader
{
    public static string? TryReadHead(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return null;
        }

        var gitDirectory = LocateGitDirectory(workingDirectory);
        if (gitDirectory is null)
        {
            return null;
        }

        return ReadHead(gitDirectory);
    }

    private static string? LocateGitDirectory(string startDirectory)
    {
        try
        {
            var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                if (File.Exists(candidate))
                {
                    var content = File.ReadAllText(candidate).Trim();
                    if (content.StartsWith("gitdir:", StringComparison.Ordinal))
                    {
                        var rest = content["gitdir:".Length..].Trim();
                        var resolved = Path.IsPathRooted(rest) ? rest : Path.Combine(directory.FullName, rest);
                        return Directory.Exists(resolved) ? resolved : null;
                    }
                }

                directory = directory.Parent;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static string? ReadHead(string gitDirectory)
    {
        try
        {
            var headPath = Path.Combine(gitDirectory, "HEAD");
            if (!File.Exists(headPath))
            {
                return null;
            }

            var head = File.ReadAllText(headPath).Trim();
            if (head.StartsWith("ref:", StringComparison.Ordinal))
            {
                var refPath = head["ref:".Length..].Trim();
                var resolved = Path.Combine(gitDirectory, refPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(resolved))
                {
                    return File.ReadAllText(resolved).Trim();
                }

                var packedRefs = Path.Combine(gitDirectory, "packed-refs");
                if (File.Exists(packedRefs))
                {
                    foreach (var line in File.ReadAllLines(packedRefs))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith('^'))
                        {
                            continue;
                        }

                        var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
                        if (parts.Length == 2 && parts[1] == refPath)
                        {
                            return parts[0];
                        }
                    }
                }

                return null;
            }

            return head;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

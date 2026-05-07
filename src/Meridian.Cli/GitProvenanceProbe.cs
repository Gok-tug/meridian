using System.Diagnostics;
using Meridian.Abstractions;

namespace Meridian.Cli;

internal static class GitProvenanceProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public static GraphProvenance Capture(string? workingDirectory, DateTimeOffset generatedAt)
    {
        var directory = ResolveWorkingDirectory(workingDirectory);
        if (directory is null)
        {
            return new GraphProvenance { GeneratedAt = generatedAt };
        }

        var commit = TryRunGit(directory, "rev-parse HEAD");
        if (commit is null)
        {
            return new GraphProvenance { GeneratedAt = generatedAt };
        }

        var branch = TryRunGit(directory, "rev-parse --abbrev-ref HEAD");
        var status = TryRunGit(directory, "status --porcelain");
        return new GraphProvenance
        {
            GeneratedAt = generatedAt,
            GitCommit = commit,
            GitBranch = string.IsNullOrWhiteSpace(branch) || branch == "HEAD" ? null : branch,
            GitDirty = status is null ? null : !string.IsNullOrEmpty(status)
        };
    }

    private static string? ResolveWorkingDirectory(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }

            if (File.Exists(fullPath))
            {
                return Path.GetDirectoryName(fullPath);
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

    private static string? TryRunGit(string workingDirectory, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            return output.Trim();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

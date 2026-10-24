namespace Polifemo.Platform.Fixtures;

/// <summary>Read-only facts about a working directory, for result provenance.</summary>
public static class GitInfo
{
    /// <summary>
    /// The full HEAD commit of the repository whose root is <paramref name="directory"/>, or null. A folder that merely
    /// sits inside another repository does not count: it must not be reported with that repository's commit.
    /// </summary>
    public static async Task<string?> HeadCommitAsync(string directory, CancellationToken ct = default)
    {
        try
        {
            var cdup = await Git.TryAsync(directory, ["rev-parse", "--show-cdup"], ct: ct);
            if (cdup.ExitCode != 0 || cdup.StdOut.Trim().Length != 0) return null;
            var r = await Git.TryAsync(directory, ["rev-parse", "--verify", "--quiet", "HEAD"], ct: ct);
            return r.ExitCode == 0 ? r.StdOut.Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}

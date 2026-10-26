using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Privacy;

/// <summary>
/// Graphs, vectors, checkpoints, caches and logs are source-derived and confidential. They live only under the
/// git-ignored <c>.index/</c> folder, owner-only (<c>0700</c> directories, <c>0600</c> files). On Windows the
/// profile ACLs apply instead and the mode checks are skipped (docs/privacy.md).
/// </summary>
public static class ProtectedStorage
{
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode GroupAndOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    /// <summary>Creates the directory (and missing parents under <c>.index/</c>) owner-only. Anything outside <c>.index/</c> is refused.</summary>
    public static string EnsurePrivateDirectory(string repositoryRoot, string path)
    {
        var indexRoot = Path.GetFullPath(Path.Combine(repositoryRoot, ".index"));
        var full = Path.GetFullPath(path);
        if (full != indexRoot && !full.StartsWith(indexRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new PrivacyPolicyException("STORAGE_OUTSIDE_INDEX", "Derived data may only be stored under .index/.");

        Directory.CreateDirectory(indexRoot);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(indexRoot, PrivateDirectory);
        var current = indexRoot;
        foreach (var segment in Path.GetRelativePath(indexRoot, full).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            Directory.CreateDirectory(current);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(current, PrivateDirectory);
        }
        return full;
    }

    public static async Task WritePrivateFileAsync(string path, string content, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Created with the final mode, so there is no moment at which the file is group- or world-readable.
            await using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                UnixCreateMode = PrivateFile,
            });
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content.AsMemory(), ct);
            return;
        }
        await File.WriteAllTextAsync(path, content, ct);
    }

    /// <summary>Throws <c>STORAGE_NOT_PRIVATE</c> when group or others can access the file or directory.</summary>
    public static void VerifyPrivate(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        if ((File.GetUnixFileMode(path) & GroupAndOther) != 0)
            throw new PrivacyPolicyException("STORAGE_NOT_PRIVATE", $"'{path}' is accessible to group or others; derived data must be owner-only.");
    }
}

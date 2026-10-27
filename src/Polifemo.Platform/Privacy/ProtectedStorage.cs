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
        var (indexRoot, full) = ResolveInsideIndex(repositoryRoot, path);
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

    /// <summary>The write boundary: <paramref name="path"/> must lie under <c>.index/</c> and no component of it may be a link.</summary>
    private static (string IndexRoot, string Full) ResolveInsideIndex(string repositoryRoot, string path)
    {
        var indexRoot = Path.GetFullPath(Path.Combine(repositoryRoot, ".index"));
        var full = Path.GetFullPath(path);
        if (full != indexRoot && !full.StartsWith(indexRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new PrivacyPolicyException("STORAGE_OUTSIDE_INDEX", "Derived data may only be stored under .index/.");

        RejectLinks(indexRoot, full);
        return (indexRoot, full);
    }

    /// <summary>
    /// Symlinks and reparse points are refused rather than followed: <c>GetFullPath</c> does not resolve them, so
    /// one could redirect derived data (or a permission change) outside <c>.index/</c>. Every existing component
    /// from <c>.index</c> down to <paramref name="full"/> is checked; components that do not exist yet are created by us.
    /// </summary>
    private static void RejectLinks(string indexRoot, string full)
    {
        var current = indexRoot;
        ThrowIfLink(current);
        foreach (var segment in Path.GetRelativePath(indexRoot, full).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            ThrowIfLink(current);
        }
    }

    private static void ThrowIfLink(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (info.Exists && (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            throw new PrivacyPolicyException("STORAGE_SYMLINK", $"'{path}' is a symbolic link or reparse point; derived data is never stored through links.");
    }

    public static async Task WritePrivateFileAsync(string repositoryRoot, string path, string content, CancellationToken ct = default)
    {
        path = ResolveInsideIndex(repositoryRoot, path).Full;
        if (!OperatingSystem.IsWindows())
        {
            // New files get the final mode at creation. An existing file keeps its old mode, so the mode is set on the
            // opened handle and the content is truncated only afterwards: old, wider permissions never apply to new data.
            await using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.Write,
                UnixCreateMode = PrivateFile,
            });
            File.SetUnixFileMode(stream.SafeFileHandle, PrivateFile);
            stream.SetLength(0);
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

using System.Security.Cryptography;

namespace Polifemo.Platform.TokenSave;

/// <summary>Worktree-relative paths that disagree with the graph. Empty lists mean the source matched.</summary>
internal sealed record SourceVerification(IReadOnlyList<string> MissingPaths, IReadOnlyList<string> ChangedPaths, bool StatChanged)
{
    public bool Ok => MissingPaths.Count == 0 && ChangedPaths.Count == 0 && !StatChanged;

    /// <summary>Every disagreeing path once, sorted ordinal.</summary>
    public IReadOnlyList<string> AllPaths => MissingPaths.Concat(ChangedPaths).Distinct().Order(StringComparer.Ordinal).ToList();
}

/// <summary>
/// Pairs the graph with the worktree (research R5): stat every indexed file, hash every file and compare with
/// <c>files.content_hash</c>, then stat every file again. Graph to disk only: files TokenSave skipped are not
/// checked. The bytes are hashed and dropped, never kept or logged (FR-012).
/// </summary>
internal static class SourceVerifier
{
    public static SourceVerification Verify(string worktree, IReadOnlyList<FileRow> files, CancellationToken ct)
    {
        var root = Path.GetFullPath(worktree);
        var missing = new List<string>();
        var changed = new List<string>();
        var located = new List<(FileRow Row, string Full, FileSnapshot Before)>();

        foreach (var row in files)
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.GetFullPath(Path.Combine(root, row.Path));
            // A path that leaves the worktree is never read; it cannot be part of this worktree's source.
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full))
            {
                missing.Add(row.Path);
                continue;
            }
            located.Add((row, full, FileSnapshot.Of(full)));
        }

        foreach (var (row, full, _) in located)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(HashOf(full), row.ContentHash, StringComparison.OrdinalIgnoreCase))
                changed.Add(row.Path);
        }

        var statChanged = false;
        foreach (var (row, full, before) in located)
        {
            if (FileSnapshot.Of(full) == before) continue;
            statChanged = true;
            if (!changed.Contains(row.Path)) changed.Add(row.Path);
        }

        return new SourceVerification(missing, changed, statChanged);
    }

    private static string? HashOf(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

using Polifemo.Contracts.Hashing;
using Polifemo.Contracts.Identity;

namespace Polifemo.Contracts.Scope;

/// <summary>
/// The exact file contents of a worktree: normalized path and <see cref="FileHash"/> of every file. A dirty
/// worktree and a clean one at the same Git HEAD have different manifests, so a snapshot id derived from the
/// manifest never confuses them.
/// </summary>
public sealed class ContentManifest
{
    public string ManifestId { get; }

    private ContentManifest(string manifestId) => ManifestId = manifestId;

    public static ContentManifest Create(IEnumerable<KeyValuePair<string, FileHash>> files)
    {
        var entries = new SortedDictionary<string, FileHash>(StringComparer.Ordinal);
        foreach (var (path, hash) in files)
        {
            if (!entries.TryAdd(Normalize.Path(path), hash))
                throw new ArgumentException("Two manifest entries normalize to the same path.", nameof(files));
        }

        var fields = new List<HashField>();
        foreach (var (path, hash) in entries)
        {
            fields.Add(path);
            fields.Add(hash.Value);
        }
        return new ContentManifest(Hashing.Hashing.Hash("cm/v1", [.. fields]));
    }

    /// <summary>The snapshot id of this content at a source commit. Branch, worktree and repository are not inputs.</summary>
    public string SnapshotId(string sourceCommit)
    {
        if (string.IsNullOrWhiteSpace(sourceCommit)) throw new ArgumentException("Source commit is required.", nameof(sourceCommit));
        return Hashing.Hashing.Hash("sn/v1", sourceCommit, ManifestId);
    }
}

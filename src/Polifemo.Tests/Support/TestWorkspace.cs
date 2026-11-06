using System.Security.Cryptography;

namespace Polifemo.Tests.Support;

/// <summary>One file of a TokenSave store, for the store-unchanged tests (SC-002).</summary>
public sealed record StoreEntry(string RelativePath, long Length, string Sha256)
{
    /// <summary>The zero-length <c>-wal</c>/<c>-shm</c> files SQLite may create when it opens a WAL database.</summary>
    public bool IsSidecar => RelativePath.EndsWith("-wal", StringComparison.Ordinal) || RelativePath.EndsWith("-shm", StringComparison.Ordinal);
}

/// <summary>Per-test scratch directory under .index/test/&lt;guid&gt;/ (git-ignored).</summary>
public sealed class TestWorkspace : IDisposable
{
    public string Path { get; }

    public TestWorkspace()
    {
        Path = System.IO.Path.Combine(RepoRoot(), ".index", "test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (global.json) not found.");
    }

    /// <summary>Every file under <paramref name="tokensaveDir"/>, sorted by relative path.</summary>
    public static IReadOnlyList<StoreEntry> StoreDigest(string tokensaveDir) =>
        Directory.EnumerateFiles(tokensaveDir, "*", SearchOption.AllDirectories)
            .Select(f => new StoreEntry(
                System.IO.Path.GetRelativePath(tokensaveDir, f).Replace('\\', '/'),
                new FileInfo(f).Length,
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f)))))
            .OrderBy(e => e.RelativePath, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Asserts the store is unchanged: the same files with the same bytes, except the sidecars SQLite may create
    /// when it opens a WAL database read-only. Those must carry no data: <c>-wal</c> is empty and <c>-shm</c>
    /// (the 32 KiB WAL index) records zero frames, so a sidecar holding database content fails.
    /// </summary>
    public static void AssertStoreUnchanged(string tokensaveDir, IReadOnlyList<StoreEntry> before)
    {
        var after = StoreDigest(tokensaveDir);
        Assert.Equal(before.Where(e => !e.IsSidecar), after.Where(e => !e.IsSidecar));
        foreach (var sidecar in after.Where(e => e.IsSidecar && !before.Contains(e)))
            Assert.True(CarriesNoData(System.IO.Path.Combine(tokensaveDir, sidecar.RelativePath)), $"{sidecar.RelativePath} carries data");
    }

    private static bool CarriesNoData(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0) return true;
        // WAL-index header: mxFrame is the 32-bit field at offset 16 (sqlite.org/walformat.html).
        return path.EndsWith("-shm", StringComparison.Ordinal) && bytes.Length >= 20 && BitConverter.ToUInt32(bytes, 16) == 0;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

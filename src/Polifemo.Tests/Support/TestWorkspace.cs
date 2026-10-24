namespace Polifemo.Tests.Support;

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

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

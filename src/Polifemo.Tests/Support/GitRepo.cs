using Polifemo.Platform.Fixtures;

namespace Polifemo.Tests.Support;

/// <summary>A tiny git repository for tests, with the same isolated configuration the product uses.</summary>
public sealed class GitRepo
{
    public string Path { get; }

    private GitRepo(string path) => Path = path;

    public static async Task<GitRepo> CreateAsync(string path)
    {
        Directory.CreateDirectory(path);
        await Git.RunAsync(path, ["init", "-q", "-b", "main"]);
        return new GitRepo(path);
    }

    public string File(string relative) => System.IO.Path.Combine(Path, relative);

    public void Write(string relative, byte[] bytes)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(File(relative))!);
        System.IO.File.WriteAllBytes(File(relative), bytes);
    }

    public void Write(string relative, string text) => Write(relative, System.Text.Encoding.UTF8.GetBytes(text));

    public async Task<string> CommitAllAsync(string message = "c")
    {
        var env = Git.WithDate("2026-01-01T00:00:00Z");
        await Git.RunAsync(Path, ["add", "--all"], env);
        await Git.RunAsync(Path, ["-c", "commit.gpgsign=false", "commit", "-q", "--no-verify", "-m", message], env);
        return await Head();
    }

    public Task<string> Head() => Git.RunAsync(Path, ["rev-parse", "HEAD"]);

    public Task<string> Run(params string[] args) => Git.RunAsync(Path, args);

    /// <summary>Full status including ignored files, the same view the preparer uses to prove the source is untouched.</summary>
    public Task<string> StatusAsync() =>
        Git.RunAsync(Path, ["--no-optional-locks", "status", "--porcelain=v1", "--ignored", "--untracked-files=all"]);
}

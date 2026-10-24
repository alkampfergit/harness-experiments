using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Fixtures;

public sealed record BuiltFixture(string Commit, string TreeSha, string RepositoryPath);

/// <summary>
/// Turns the plain fixture files into a git repository whose commit SHA is deterministic
/// (fixed identity and dates, sorted content, no user or system git configuration; research R6).
/// Only the content folders become repository content; mutations, expectations and provenance
/// stay outside it.
/// </summary>
public static class FixtureBuilder
{
    public const string CommitDate = "2026-01-01T00:00:00Z";
    private static readonly string[] ContentFolders = ["src", "docs", "tokensave-docs"];

    public static async Task<BuiltFixture> BuildAsync(string fixtureDir, string outDir, CancellationToken ct = default)
    {
        fixtureDir = Path.GetFullPath(fixtureDir);
        outDir = Path.GetFullPath(outDir);
        if (!Directory.Exists(fixtureDir))
            throw new FixturePreparationException("FIX_NOT_FOUND", $"Fixture directory '{fixtureDir}' does not exist.");
        if (!UnderIndexFolder(outDir))
            throw new FixturePreparationException("FIX_BAD_OUT", $"Output '{outDir}' must be inside a '.index' folder.");

        if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        Directory.CreateDirectory(outDir);

        foreach (var folder in ContentFolders)
        {
            var source = Path.Combine(fixtureDir, folder);
            if (Directory.Exists(source)) CopyTree(source, Path.Combine(outDir, folder));
        }

        var env = Git.WithDate(CommitDate);
        await Git.RunAsync(outDir, ["init", "-q", "-b", "main"], env, ct);
        await Git.RunAsync(outDir, ["add", "--all"], env, ct);
        await Git.RunAsync(outDir,
            ["-c", "commit.gpgsign=false", "commit", "-q", "--no-verify", "-m", "fixture: synthetic-csharp"], env, ct);

        var commit = await Git.RunAsync(outDir, ["rev-parse", "HEAD"], env, ct);
        var tree = await Git.RunAsync(outDir, ["rev-parse", "HEAD^{tree}"], env, ct);
        return new BuiltFixture(commit, tree, outDir);
    }

    internal static bool UnderIndexFolder(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".index");

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source).Order(StringComparer.Ordinal))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(source).Order(StringComparer.Ordinal))
            CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}

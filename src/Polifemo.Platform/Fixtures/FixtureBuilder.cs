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
        fixtureDir = ResolveLinks(fixtureDir);
        outDir = Path.GetFullPath(outDir);
        if (!Directory.Exists(fixtureDir))
            throw new FixturePreparationException("FIX_NOT_FOUND", $"Fixture directory '{fixtureDir}' does not exist.");
        if (!UnderIndexFolder(outDir))
            throw new FixturePreparationException("FIX_BAD_OUT", $"Output '{outDir}' must be inside a '.index' folder (not the '.index' root itself).");

        if (Overlaps(fixtureDir, ResolveLinks(outDir)))
            throw new FixturePreparationException("FIX_BAD_OUT", $"Output '{outDir}' and fixture '{fixtureDir}' must be disjoint folders.");

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

    /// <summary>True when the path lies strictly inside a '.index' folder (the '.index' root itself is rejected).</summary>
    internal static bool UnderIndexFolder(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        var last = Array.LastIndexOf(segments, ".index");
        return last >= 0 && last < segments.Length - 1;
    }

    /// <summary>True when the two paths are equal or one lies inside the other.</summary>
    internal static bool Overlaps(string a, string b)
    {
        a = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        b = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return IsSameOrInside(a, b) || IsSameOrInside(b, a);

        static bool IsSameOrInside(string parent, string child) =>
            child == parent || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Full path with every existing symlink or reparse point resolved, so that two spellings of
    /// the same directory compare equal. Components that do not exist yet are kept as written.
    /// </summary>
    internal static string ResolveLinks(string path)
    {
        var current = Path.GetFullPath(path);
        var root = Path.GetPathRoot(current) ?? string.Empty;
        var resolved = root;
        foreach (var segment in current[root.Length..].Split(
                     Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, segment);
            var info = new DirectoryInfo(resolved);
            if (!info.Exists) continue;
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null) resolved = target.FullName;
        }
        return resolved.TrimEnd(Path.DirectorySeparatorChar);
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source).Order(StringComparer.Ordinal))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(source).Order(StringComparer.Ordinal))
            CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}

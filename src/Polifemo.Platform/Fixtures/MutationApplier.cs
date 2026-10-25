using System.Text.Json;
using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Fixtures;

public sealed record MutationResult(string Base, string? Commit, string TreeSha, IReadOnlyList<string> ChangedPaths);

/// <summary>
/// Applies a declarative mutation (<c>mutation.json</c> plus payload files) byte-exactly and, unless
/// asked not to, commits it with fixed dates so the same base always yields the same commit.
/// </summary>
public static class MutationApplier
{
    public const string CommitDate = "2026-01-02T00:00:00Z";
    private const string InvalidCode = "MUT_INVALID";
    private const string RevParse = "rev-parse";

    private sealed record Op(string Kind, string? Path, string? PayloadFile, string? From, string? To, int? AtLine);

    public static async Task<MutationResult> ApplyAsync(
        string worktree, string mutationDir, bool commit = true, CancellationToken ct = default)
    {
        worktree = System.IO.Path.GetFullPath(worktree);
        mutationDir = System.IO.Path.GetFullPath(mutationDir);
        if (!FixtureBuilder.UnderIndexFolder(worktree))
            throw new FixturePreparationException("MUT_BAD_WORKTREE", $"Worktree '{worktree}' must be inside a '.index' folder; corpora are read only.");
        var doc = await ReadAsync(mutationDir, ct);

        var head = await Git.RunAsync(worktree, [RevParse, "HEAD"], ct: ct);
        var expectedBase = doc.Base == "initial"
            ? (await Git.RunAsync(worktree, ["rev-list", "--max-parents=0", "HEAD"], ct: ct)).Split('\n')[0].Trim()
            : doc.Base;
        if (head != expectedBase)
            throw new FixturePreparationException("MUT_BASE_MISMATCH",
                $"Mutation '{doc.Name}' expects base {doc.Base} ({expectedBase}) but HEAD is {head}.");

        var changed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var op in doc.Ops) Apply(op, worktree, mutationDir, changed);

        string? sha = null;
        if (commit)
        {
            var env = Git.WithDate(CommitDate);
            await Git.RunAsync(worktree, ["add", "--all", "--", .. changed], env, ct);
            await Git.RunAsync(worktree,
                ["-c", "commit.gpgsign=false", "commit", "-q", "--no-verify", "-m", $"mutation: {doc.Name}"], env, ct);
            sha = await Git.RunAsync(worktree, [RevParse, "HEAD"], env, ct);
        }

        var tree = commit
            ? await Git.RunAsync(worktree, [RevParse, "HEAD^{tree}"], ct: ct)
            : await WorkingTreeShaAsync(worktree, ct);
        return new MutationResult(head, sha, tree, changed.ToList());
    }

    /// <summary>Tree SHA of the working tree without committing: stages into a throw-away index file.</summary>
    private static async Task<string> WorkingTreeShaAsync(string worktree, CancellationToken ct)
    {
        var gitDir = await Git.RunAsync(worktree, [RevParse, "--absolute-git-dir"], ct: ct);
        var temp = System.IO.Path.Combine(gitDir, $"polifemo-index-{Guid.NewGuid():N}");
        var env = new Dictionary<string, string>(Git.IsolatedEnvironment) { ["GIT_INDEX_FILE"] = temp };
        try
        {
            await Git.RunAsync(worktree, ["add", "--all"], env, ct);
            return await Git.RunAsync(worktree, ["write-tree"], env, ct);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static async Task<(string Name, string Base, List<Op> Ops)> ReadAsync(string mutationDir, CancellationToken ct)
    {
        var file = System.IO.Path.Combine(mutationDir, "mutation.json");
        if (!File.Exists(file))
            throw new FixturePreparationException(InvalidCode, $"'{file}' does not exist.");

        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file, ct));
            var root = doc.RootElement;
            var ops = root.GetProperty("ops").EnumerateArray().Select(o => new Op(
                o.GetProperty("op").GetString()!,
                Str(o, "path"), Str(o, "payloadFile"), Str(o, "from"), Str(o, "to"),
                o.TryGetProperty("atLine", out var l) ? l.GetInt32() : null)).ToList();
            return (root.GetProperty("name").GetString()!, root.GetProperty("base").GetString()!, ops);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FixturePreparationException(InvalidCode, $"'{file}' is not a valid mutation: {ex.Message}");
        }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static void Apply(Op op, string worktree, string mutationDir, SortedSet<string> changed)
    {
        switch (op.Kind)
        {
            case "write":
                Write(Resolve(worktree, op.Path), Payload(mutationDir, op), requireExisting: false);
                changed.Add(Relative(worktree, op.Path));
                break;
            case "replaceBytes":
                Write(Resolve(worktree, op.Path), Payload(mutationDir, op), requireExisting: true);
                changed.Add(Relative(worktree, op.Path));
                break;
            case "delete":
                var del = Resolve(worktree, op.Path);
                if (!File.Exists(del)) throw new FixturePreparationException(InvalidCode, $"Cannot delete missing '{op.Path}'.");
                File.Delete(del);
                changed.Add(Relative(worktree, op.Path));
                break;
            case "rename":
                var from = Resolve(worktree, op.From);
                var to = Resolve(worktree, op.To);
                if (!File.Exists(from)) throw new FixturePreparationException(InvalidCode, $"Cannot rename missing '{op.From}'.");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
                File.Move(from, to, overwrite: false);
                changed.Add(Relative(worktree, op.From));
                changed.Add(Relative(worktree, op.To));
                break;
            case "insertLines":
                InsertLines(Resolve(worktree, op.Path), Payload(mutationDir, op), op.AtLine ?? 0);
                changed.Add(Relative(worktree, op.Path));
                break;
            default:
                throw new FixturePreparationException(InvalidCode, $"Unknown op '{op.Kind}'.");
        }
    }

    private static byte[] Payload(string mutationDir, Op op)
    {
        if (op.PayloadFile is null) throw new FixturePreparationException(InvalidCode, $"Op '{op.Kind}' needs a payloadFile.");
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(mutationDir, op.PayloadFile));
        if (!path.StartsWith(mutationDir + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new FixturePreparationException("MUT_BAD_PATH", $"Payload '{op.PayloadFile}' escapes the mutation directory.");
        return File.ReadAllBytes(path);
    }

    private static void Write(string path, byte[] bytes, bool requireExisting)
    {
        if (requireExisting && !File.Exists(path))
            throw new FixturePreparationException(InvalidCode, $"Cannot replace missing '{path}'.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Inserts the payload before 1-based line <paramref name="atLine"/>, scanning raw LF bytes.</summary>
    private static void InsertLines(string path, byte[] payload, int atLine)
    {
        if (atLine < 1) throw new FixturePreparationException(InvalidCode, "insertLines needs atLine >= 1.");
        var original = File.ReadAllBytes(path);
        var offset = 0;
        for (var line = 1; line < atLine; line++)
        {
            var next = Array.IndexOf(original, (byte)'\n', offset);
            if (next < 0) throw new FixturePreparationException(InvalidCode, $"'{path}' has fewer than {atLine} lines.");
            offset = next + 1;
        }
        File.WriteAllBytes(path, [.. original.AsSpan(0, offset), .. payload, .. original.AsSpan(offset)]);
    }

    /// <summary>Resolves a mutation path and rejects anything that leaves the worktree or touches .git.</summary>
    private static string Resolve(string worktree, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative))
            throw new FixturePreparationException("MUT_BAD_PATH", $"'{relative}' must be a relative path.");
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(worktree, relative));
        var inside = full.StartsWith(worktree + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal);
        var gitDir = System.IO.Path.Combine(worktree, ".git");
        if (!inside || full == gitDir || full.StartsWith(gitDir + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new FixturePreparationException("MUT_BAD_PATH", $"'{relative}' is outside the worktree.");
        return full;
    }

    private static string Relative(string worktree, string? path) =>
        System.IO.Path.GetRelativePath(worktree, Resolve(worktree, path)).Replace('\\', '/');
}

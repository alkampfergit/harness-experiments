using System.Text.Json;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.Processes;

namespace Polifemo.Platform.TokenSave;

public sealed record ResolvedDatabase(string DatabasePath, string Resolution);

/// <summary>
/// Finds the active TokenSave database of a worktree (research R3): branch metadata when present,
/// <c>.tokensave/tokensave.db</c> only when it is absent, and an explicit error otherwise.
/// </summary>
public sealed class ActiveDatabaseResolver(Func<string, CancellationToken, Task<string?>> currentBranch)
{
    public const string DefaultDatabaseFile = "tokensave.db";

    public ActiveDatabaseResolver() : this(GitBranchAsync) { }

    public async Task<ResolvedDatabase> ResolveAsync(string worktree, CancellationToken ct = default)
    {
        var tokensaveDir = Path.GetFullPath(Path.Combine(worktree, ".tokensave"));
        var metaPath = Path.Combine(tokensaveDir, "branch-meta.json");

        if (!File.Exists(metaPath))
            return Existing(Path.Combine(tokensaveDir, DefaultDatabaseFile), "default");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(await File.ReadAllTextAsync(metaPath, ct));
        }
        catch (JsonException ex)
        {
            throw new ActiveDatabaseResolutionException("TS_META_MALFORMED", $"{metaPath} is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ActiveDatabaseResolutionException("TS_META_MALFORMED", $"{metaPath} must contain a JSON object.");

            var branch = await currentBranch(worktree, ct);
            var dbFile = FindDbFile(root, branch)
                ?? throw new ActiveDatabaseResolutionException("TS_META_NO_DB",
                    $"{metaPath} names no database for branch '{branch ?? "(detached)"}' or for the default branch.");

            var full = Path.GetFullPath(Path.Combine(tokensaveDir, dbFile));
            if (!full.StartsWith(tokensaveDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ActiveDatabaseResolutionException("TS_META_NO_DB", $"db_file '{dbFile}' points outside .tokensave/.");

            return Existing(full, "branch-meta");
        }
    }

    private static string? FindDbFile(JsonElement root, string? branch)
    {
        if (!root.TryGetProperty("branches", out var branches) || branches.ValueKind != JsonValueKind.Object)
            return null;

        if (branch is not null && branches.TryGetProperty(branch, out var current))
            return DbFileOf(current);

        if (root.TryGetProperty("default_branch", out var def) && def.ValueKind == JsonValueKind.String
            && branches.TryGetProperty(def.GetString()!, out var fallback))
            return DbFileOf(fallback);

        return null;
    }

    private static string? DbFileOf(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty("db_file", out var f)
        && f.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(f.GetString())
            ? f.GetString()
            : null;

    private static ResolvedDatabase Existing(string path, string resolution) =>
        File.Exists(path)
            ? new ResolvedDatabase(path, resolution)
            : throw new ActiveDatabaseResolutionException("TS_DB_MISSING", $"TokenSave database '{path}' does not exist.");

    private static async Task<string?> GitBranchAsync(string worktree, CancellationToken ct)
    {
        try
        {
            var r = await ProcessRunner.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], worktree, ct: ct);
            var name = r.StdOut.Trim();
            return r.ExitCode == 0 && name is not ("" or "HEAD") ? name : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}

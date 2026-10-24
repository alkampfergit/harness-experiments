using Polifemo.Contracts.Errors;
using System.Text.RegularExpressions;

namespace Polifemo.Platform.Fixtures;

public sealed record CreatedWorktree(string Path, bool Dirty, IReadOnlyList<string> ChangedPaths);

/// <summary>
/// Adds a second git worktree of a prepared clone. The worktree is registered in the clone's own
/// <c>.git</c>, never in the original sample, and lives next to the clone under <c>.index/work/</c>.
/// </summary>
public static partial class WorktreeFactory
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex NamePattern();

    public static async Task<CreatedWorktree> AddAsync(
        string workDir, string name, string? dirtyFromMutationDir = null, CancellationToken ct = default)
    {
        workDir = System.IO.Path.GetFullPath(workDir);
        if (!NamePattern().IsMatch(name))
            throw new FixturePreparationException("PREP_BAD_NAME", $"'{name}' is not a valid worktree name.");

        var target = $"{workDir}+{name}";
        if (Directory.Exists(target))
            throw new FixturePreparationException("PREP_TARGET_EXISTS", $"'{target}' already exists.");

        await Git.RunAsync(workDir, ["worktree", "add", "-q", "--detach", target, "HEAD"], ct: ct);

        IReadOnlyList<string> changed = [];
        if (dirtyFromMutationDir is not null)
            changed = (await MutationApplier.ApplyAsync(target, dirtyFromMutationDir, commit: false, ct)).ChangedPaths;
        return new CreatedWorktree(target, dirtyFromMutationDir is not null, changed);
    }
}

using Polifemo.Contracts.Provenance;
using Polifemo.Platform.Fixtures;

namespace Polifemo.Cli;

/// <summary>Every CLI result that describes a worktree says which snapshot it describes (FR-026).</summary>
public static class ResultProvenance
{
    public static async Task<SnapshotProvenance> ForWorktreeAsync(
        string worktree, string extractorVersion, string? commit = null, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(worktree);
        var head = commit ?? await GitInfo.HeadCommitAsync(full, ct) ?? "unknown";
        var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar));
        var branch = await GitInfo.BranchAsync(full, ct) ?? "detached";
        var scope = new SourceScope("local", name, branch, name, head);
        return new SnapshotProvenance(scope, head, head, extractorVersion);
    }
}

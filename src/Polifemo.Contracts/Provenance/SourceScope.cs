namespace Polifemo.Contracts.Provenance;

/// <summary>Where a piece of source lives. Authorization is carried, not enforced (F02).</summary>
public sealed record SourceScope
{
    public string AuthorizationBoundary { get; }
    public string RepositoryId { get; }
    public string BranchRef { get; }
    public string WorktreeId { get; }
    public string SnapshotId { get; }

    public SourceScope(string authorizationBoundary, string repositoryId, string branchRef, string worktreeId, string snapshotId)
    {
        AuthorizationBoundary = Require(authorizationBoundary, nameof(authorizationBoundary));
        RepositoryId = Require(repositoryId, nameof(repositoryId));
        BranchRef = Require(branchRef, nameof(branchRef));
        WorktreeId = Require(worktreeId, nameof(worktreeId));
        SnapshotId = Require(snapshotId, nameof(snapshotId));
    }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Must be non-empty.", name) : value;
}

public sealed record SnapshotProvenance(SourceScope Scope, string SnapshotId, string SourceCommit, string ExtractorVersion);

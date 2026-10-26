using Polifemo.Contracts.Errors;
using Polifemo.Contracts.Identity;
using Polifemo.Contracts.Provenance;

namespace Polifemo.Contracts.Scope;

/// <summary>What a caller asks for. Repository, branch, worktree and snapshot stay four separate ids.</summary>
public sealed record ScopeRequest(
    string AuthorizationBoundary, string RepositoryId, string BranchRef, string WorktreeId, string? SnapshotId = null);

public enum ScopePurpose
{
    /// <summary>Reads a published snapshot.</summary>
    Query,

    /// <summary>Writes a snapshot that is not published yet.</summary>
    Indexing,
}

/// <summary>
/// A scope that passed <see cref="ScopeResolver"/>. It has no public constructor, so every retrieval arm, file
/// lookup, expansion and cache that takes one has been handed a validated authorization and snapshot context.
/// </summary>
public sealed class ResolvedScope
{
    public const string BoundaryField = "authorization_boundary";
    public const string RepositoryField = "repository_id";
    public const string BranchField = "branch_ref";
    public const string WorktreeField = "worktree_id";
    public const string SnapshotField = "snapshot_id";

    public SourceScope Scope { get; }
    public ScopePurpose Purpose { get; }

    internal ResolvedScope(SourceScope scope, ScopePurpose purpose)
    {
        Scope = scope;
        Purpose = purpose;
    }

    /// <summary>
    /// Exact-match terms every retrieval arm applies while it generates candidates, never only after ranking.
    /// </summary>
    public IReadOnlyDictionary<string, string> FilterTerms => new Dictionary<string, string>
    {
        [BoundaryField] = Scope.AuthorizationBoundary,
        [RepositoryField] = Scope.RepositoryId,
        [BranchField] = Scope.BranchRef,
        [WorktreeField] = Scope.WorktreeId,
        [SnapshotField] = Scope.SnapshotId,
    };

    public bool Contains(SourceScope other) => Scope == other;

    /// <summary>Defense in depth for results coming back from a store: a hit outside the scope is a defect, never data.</summary>
    public void RequireContains(SourceScope other)
    {
        if (!Contains(other))
            throw new ScopeException("SCOPE_LEAK", "A result outside the resolved scope was returned and has been dropped.");
    }
}

/// <summary>
/// Cache and de-duplication keys always start with the authorization boundary, so identical content in two
/// boundaries never shares a payload, vector or cache entry (constitution VI).
/// </summary>
public static class BoundaryKey
{
    public static string Create(ResolvedScope scope, string purpose, string contentKey) =>
        Hashing.Hashing.Hash("bk/v1", scope.Scope.AuthorizationBoundary, purpose, contentKey);

    public static string ForEmbedding(ResolvedScope scope, EmbeddingInputHash input) => Create(scope, "embedding", input.Value);
}

/// <summary>
/// The in-memory registry of what may be read and what has been published. Persisting it belongs to the
/// publication feature (F04); this class fixes the rules that storage must keep.
/// </summary>
public sealed class ScopeRegistry
{
    private readonly object _gate = new();
    private readonly HashSet<(string Boundary, string Repository, string Branch, string Worktree)> _authorized = [];
    private readonly Dictionary<(string Boundary, string Repository, string Branch, string Worktree), List<string>> _published = [];

    public void Authorize(string authorizationBoundary, string repositoryId, string branchRef, string worktreeId)
    {
        var scope = new SourceScope(authorizationBoundary, repositoryId, branchRef, worktreeId, "-");
        lock (_gate) _authorized.Add(Key(scope));
    }

    /// <summary>Marks a snapshot readable. The last published snapshot of a worktree is its current one.</summary>
    public void Publish(SourceScope scope)
    {
        lock (_gate)
        {
            if (!_authorized.Contains(Key(scope)))
                throw new ScopeException(ScopeResolver.NotAuthorized, ScopeResolver.NotAuthorizedMessage);
            if (!_published.TryGetValue(Key(scope), out var snapshots)) _published[Key(scope)] = snapshots = [];
            snapshots.Remove(scope.SnapshotId);
            snapshots.Add(scope.SnapshotId);
        }
    }

    internal bool IsAuthorized(ScopeRequest r)
    {
        lock (_gate) return _authorized.Contains((r.AuthorizationBoundary, r.RepositoryId, r.BranchRef, r.WorktreeId));
    }

    internal string? PublishedSnapshot(ScopeRequest r)
    {
        lock (_gate)
        {
            if (!_published.TryGetValue((r.AuthorizationBoundary, r.RepositoryId, r.BranchRef, r.WorktreeId), out var snapshots)) return null;
            if (r.SnapshotId is null) return snapshots.Count > 0 ? snapshots[^1] : null;
            return snapshots.Contains(r.SnapshotId) ? r.SnapshotId : null;
        }
    }

    private static (string, string, string, string) Key(SourceScope s) =>
        (s.AuthorizationBoundary, s.RepositoryId, s.BranchRef, s.WorktreeId);
}

/// <summary>The one boundary indexing and queries use to turn a request into a validated scope.</summary>
public sealed class ScopeResolver(ScopeRegistry registry)
{
    public const string NotAuthorized = "SCOPE_NOT_AUTHORIZED";
    public const string NotAuthorizedMessage = "The requested scope is unknown or not authorized for this boundary.";
    public const string Invalid = "SCOPE_INVALID";
    public const string SnapshotUnpublished = "SNAPSHOT_UNPUBLISHED";
    public const string SnapshotMismatch = "SNAPSHOT_MISMATCH";

    /// <summary>
    /// Unknown scopes and scopes of another boundary fail with the same code and message, so a caller learns
    /// nothing about what exists elsewhere. An unpublished snapshot inside an authorized scope is reported as such.
    /// </summary>
    public ResolvedScope ResolveForQuery(ScopeRequest request)
    {
        RequireComplete(request);
        if (!registry.IsAuthorized(request)) throw new ScopeException(NotAuthorized, NotAuthorizedMessage);

        var snapshot = registry.PublishedSnapshot(request)
            ?? throw new ScopeException(SnapshotUnpublished,
                request.SnapshotId is null
                    ? "No snapshot has been published for this scope."
                    : "The requested snapshot is not published for this scope.");
        return new ResolvedScope(ToScope(request, snapshot), ScopePurpose.Query);
    }

    /// <summary>
    /// The snapshot being written is derived from the commit and the content manifest, so dirty content at the
    /// same HEAD is a different snapshot. A caller-supplied id must agree with the derived one.
    /// </summary>
    public ResolvedScope ResolveForIndexing(ScopeRequest request, ContentManifest manifest, string sourceCommit)
    {
        RequireComplete(request);
        if (!registry.IsAuthorized(request)) throw new ScopeException(NotAuthorized, NotAuthorizedMessage);

        var snapshot = manifest.SnapshotId(sourceCommit);
        if (request.SnapshotId is not null && request.SnapshotId != snapshot)
            throw new ScopeException(SnapshotMismatch, "The snapshot id does not match the commit and content manifest being indexed.");
        return new ResolvedScope(ToScope(request, snapshot), ScopePurpose.Indexing);
    }

    private static SourceScope ToScope(ScopeRequest r, string snapshot) =>
        new(r.AuthorizationBoundary, r.RepositoryId, r.BranchRef, r.WorktreeId, snapshot);

    private static void RequireComplete(ScopeRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.AuthorizationBoundary) || string.IsNullOrWhiteSpace(r.RepositoryId)
            || string.IsNullOrWhiteSpace(r.BranchRef) || string.IsNullOrWhiteSpace(r.WorktreeId)
            || (r.SnapshotId is not null && string.IsNullOrWhiteSpace(r.SnapshotId)))
            throw new ScopeException(Invalid, "Authorization boundary, repository, branch and worktree are all required.");
    }
}

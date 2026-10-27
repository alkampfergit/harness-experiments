using Polifemo.Contracts.Errors;
using Polifemo.Contracts.Identity;
using Polifemo.Contracts.Provenance;
using Polifemo.Contracts.Scope;

namespace Polifemo.Tests.Contracts;

/// <summary>F02-02: one scope-resolution boundary, content-bound snapshots, no leak across scopes.</summary>
public sealed class ScopeResolutionTests
{
    private const string Commit = "c0ffee";

    private static ContentManifest Manifest(string content, string path = "src/A.cs") =>
        ContentManifest.Create([new(path, FileHash.Create(System.Text.Encoding.UTF8.GetBytes(content)))]);

    private static ScopeRequest Request(string boundary = "team-a", string worktree = "wt-1", string? snapshot = null, string branch = "main") =>
        new(boundary, "repo", branch, worktree, snapshot);

    private static (ScopeRegistry Registry, ScopeResolver Resolver) Setup()
    {
        var registry = new ScopeRegistry();
        registry.Authorize("team-a", "repo", "main", "wt-1");
        return (registry, new ScopeResolver(registry));
    }

    private static void Publish(ScopeRegistry registry, ScopeResolver resolver, ScopeRequest request, string content)
    {
        var resolved = resolver.ResolveForIndexing(request, Manifest(content), Commit);
        registry.Publish(resolved.Scope);
    }

    [Fact]
    public void Manifest_is_order_independent_and_content_sensitive()
    {
        var a = ContentManifest.Create([new("a.cs", FileHash.Create("1"u8)), new("b.cs", FileHash.Create("2"u8))]);
        var reordered = ContentManifest.Create([new("b.cs", FileHash.Create("2"u8)), new("a.cs", FileHash.Create("1"u8))]);
        var edited = ContentManifest.Create([new("a.cs", FileHash.Create("1"u8)), new("b.cs", FileHash.Create("3"u8))]);
        var renamed = ContentManifest.Create([new("a.cs", FileHash.Create("1"u8)), new("c.cs", FileHash.Create("2"u8))]);

        Assert.Equal(a.ManifestId, reordered.ManifestId);
        Assert.NotEqual(a.ManifestId, edited.ManifestId);
        Assert.NotEqual(a.ManifestId, renamed.ManifestId);
    }

    [Fact]
    public void Manifest_paths_are_normalized_and_must_be_unique()
    {
        Assert.Equal(
            ContentManifest.Create([new("src/a.cs", FileHash.Create("1"u8))]).ManifestId,
            ContentManifest.Create([new(@"src\a.cs", FileHash.Create("1"u8))]).ManifestId);
        Assert.Throws<ArgumentException>(() => ContentManifest.Create([new("src/a.cs", FileHash.Create("1"u8)), new(@"src\a.cs", FileHash.Create("1"u8))]));
    }

    [Fact]
    public void Dirty_content_at_the_same_head_is_a_different_snapshot()
    {
        var clean = Manifest("class A {}");
        var dirty = Manifest("class A { int dirty; }");

        Assert.NotEqual(clean.SnapshotId(Commit), dirty.SnapshotId(Commit));
        Assert.Equal(clean.SnapshotId(Commit), Manifest("class A {}").SnapshotId(Commit));
        Assert.NotEqual(clean.SnapshotId(Commit), clean.SnapshotId("other"));
        Assert.Throws<ArgumentException>(() => clean.SnapshotId(" "));
    }

    [Fact]
    public void Unknown_and_other_boundary_scopes_fail_with_the_same_indistinguishable_error()
    {
        var (registry, resolver) = Setup();
        registry.Authorize("team-b", "repo", "main", "wt-2");

        var unknown = Assert.Throws<ScopeException>(() => resolver.ResolveForQuery(Request(worktree: "nope")));
        var foreign = Assert.Throws<ScopeException>(() => resolver.ResolveForQuery(Request(boundary: "team-a", worktree: "wt-2")));
        var stolen = Assert.Throws<ScopeException>(() => resolver.ResolveForQuery(Request(boundary: "team-c", worktree: "wt-1")));

        Assert.All(new[] { unknown, foreign, stolen }, e =>
        {
            Assert.Equal(ScopeResolver.NotAuthorized, e.Code);
            Assert.Equal(ScopeResolver.NotAuthorizedMessage, e.Message);
        });
    }

    [Fact]
    public void An_unpublished_snapshot_fails_explicitly()
    {
        var (registry, resolver) = Setup();

        Assert.Equal(ScopeResolver.SnapshotUnpublished, Assert.Throws<ScopeException>(() => resolver.ResolveForQuery(Request())).Code);

        Publish(registry, resolver, Request(), "class A {}");
        Assert.Equal(ScopeResolver.SnapshotUnpublished,
            Assert.Throws<ScopeException>(() => resolver.ResolveForQuery(Request(snapshot: "not-published"))).Code);
    }

    [Fact]
    public void Queries_resolve_the_current_published_snapshot_or_a_named_published_one()
    {
        var (registry, resolver) = Setup();
        var first = resolver.ResolveForIndexing(Request(), Manifest("v1"), Commit).Scope;
        var second = resolver.ResolveForIndexing(Request(), Manifest("v2"), Commit).Scope;
        registry.Publish(first);
        registry.Publish(second);

        Assert.Equal(second.SnapshotId, resolver.ResolveForQuery(Request()).Scope.SnapshotId);
        Assert.Equal(first.SnapshotId, resolver.ResolveForQuery(Request(snapshot: first.SnapshotId)).Scope.SnapshotId);
        Assert.Equal(ScopePurpose.Query, resolver.ResolveForQuery(Request()).Purpose);

        registry.Publish(first);
        Assert.Equal(first.SnapshotId, resolver.ResolveForQuery(Request()).Scope.SnapshotId);
    }

    [Fact]
    public void Repository_branch_worktree_and_snapshot_stay_separate_ids()
    {
        var (registry, resolver) = Setup();
        registry.Authorize("team-a", "repo", "feature", "wt-1");
        Publish(registry, resolver, Request(), "x");

        Assert.Equal(ScopeResolver.NotAuthorized, Assert.Throws<ScopeException>(() => resolver.ResolveForQuery(Request(branch: "feature") with { RepositoryId = "other" })).Code);
        Assert.Equal(ScopeResolver.SnapshotUnpublished, Assert.Throws<ScopeException>(() => resolver.ResolveForQuery(Request(branch: "feature"))).Code);
        var terms = resolver.ResolveForQuery(Request()).FilterTerms;
        Assert.Equal(["team-a", "repo", "main", "wt-1"], new[]
        {
            terms[ResolvedScope.BoundaryField], terms[ResolvedScope.RepositoryField], terms[ResolvedScope.BranchField], terms[ResolvedScope.WorktreeField],
        });
        Assert.NotEmpty(terms[ResolvedScope.SnapshotField]);
    }

    [Fact]
    public void Incomplete_requests_are_invalid()
    {
        var (_, resolver) = Setup();

        foreach (var bad in new[] { Request(boundary: " "), Request(worktree: ""), Request(snapshot: " "), Request(branch: "") })
        {
            Assert.Equal(ScopeResolver.Invalid, Assert.Throws<ScopeException>(() => resolver.ResolveForQuery(bad)).Code);
            Assert.Equal(ScopeResolver.Invalid, Assert.Throws<ScopeException>(() => resolver.ResolveForIndexing(bad, Manifest("x"), Commit)).Code);
        }
    }

    [Fact]
    public void Indexing_needs_authorization_and_a_snapshot_id_that_matches_the_content()
    {
        var (registry, resolver) = Setup();
        var manifest = Manifest("class A {}");

        Assert.Equal(ScopeResolver.NotAuthorized,
            Assert.Throws<ScopeException>(() => resolver.ResolveForIndexing(Request(boundary: "team-z"), manifest, Commit)).Code);
        Assert.Equal(ScopeResolver.SnapshotMismatch,
            Assert.Throws<ScopeException>(() => resolver.ResolveForIndexing(Request(snapshot: "forged"), manifest, Commit)).Code);

        var ok = resolver.ResolveForIndexing(Request(snapshot: manifest.SnapshotId(Commit)), manifest, Commit);
        Assert.Equal(ScopePurpose.Indexing, ok.Purpose);
        Assert.Equal(ScopeResolver.SnapshotUnpublished, Assert.Throws<ScopeException>(() => resolver.ResolveForQuery(Request(snapshot: ok.Scope.SnapshotId))).Code);
        registry.Publish(ok.Scope);
        Assert.Equal(ok.Scope, resolver.ResolveForQuery(Request(snapshot: ok.Scope.SnapshotId)).Scope);
    }

    [Fact]
    public void Publishing_outside_an_authorized_scope_is_refused()
    {
        var registry = new ScopeRegistry();

        Assert.Equal(ScopeResolver.NotAuthorized,
            Assert.Throws<ScopeException>(() => registry.Publish(new SourceScope("team-a", "repo", "main", "wt-1", "s"))).Code);
    }

    [Fact]
    public void A_resolved_scope_cannot_be_constructed_outside_the_resolver() =>
        Assert.Empty(typeof(ResolvedScope).GetConstructors());

    [Fact]
    public void A_hit_outside_the_resolved_scope_is_a_defect()
    {
        var (registry, resolver) = Setup();
        Publish(registry, resolver, Request(), "x");
        var scope = resolver.ResolveForQuery(Request());

        scope.RequireContains(scope.Scope);
        var elsewhere = new SourceScope("team-b", "repo", "main", "wt-1", scope.Scope.SnapshotId);
        Assert.True(scope.Contains(scope.Scope));
        Assert.False(scope.Contains(elsewhere));
        Assert.Equal("SCOPE_LEAK", Assert.Throws<ScopeException>(() => scope.RequireContains(elsewhere)).Code);
    }

    [Fact]
    public void Cache_and_dedup_keys_never_cross_an_authorization_boundary()
    {
        var registry = new ScopeRegistry();
        registry.Authorize("team-a", "repo", "main", "wt-1");
        registry.Authorize("team-b", "repo", "main", "wt-1");
        var resolver = new ScopeResolver(registry);
        var a = resolver.ResolveForIndexing(Request("team-a"), Manifest("same"), Commit);
        var b = resolver.ResolveForIndexing(Request("team-b"), Manifest("same"), Commit);
        var input = EmbeddingInputHash.Create("profile", "tpl", "document", "same content"u8);

        Assert.NotEqual(BoundaryKey.ForEmbedding(a, input), BoundaryKey.ForEmbedding(b, input));
        Assert.Equal(BoundaryKey.ForEmbedding(a, input), BoundaryKey.ForEmbedding(a, input));
        Assert.NotEqual(BoundaryKey.Create(a, "payload", "h"), BoundaryKey.Create(a, "vector", "h"));
        Assert.NotEqual(OccurrenceKey.Create(LogicalKey.Create("repo", "a.cs", "A", "class", null), a.Scope),
            OccurrenceKey.Create(LogicalKey.Create("repo", "a.cs", "A", "class", null), b.Scope));
    }
}

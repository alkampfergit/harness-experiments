using Polifemo.Contracts.Identity;
using Polifemo.Contracts.Provenance;
using Polifemo.Contracts.Scope;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Contracts;

/// <summary>
/// F02-02 / F02 acceptance: identical content in different scopes, and different dirty content at the same HEAD,
/// never leak across scopes in any retrieval arm, and the scope is applied before candidates exist.
/// </summary>
public sealed class ScopedRetrievalTests
{
    private const string Commit = "c0ffee";
    private const string Shared = "class Invoice { /* shared */ }";
    private const string DirtySecret = "class Invoice { /* UNPUBLISHED-DIRTY-EDIT */ }";

    private readonly ScopedTestStore _store = new();
    private readonly ScopeRegistry _registry = new();
    private readonly ScopeResolver _resolver;

    public ScopedRetrievalTests() => _resolver = new ScopeResolver(_registry);

    private ResolvedScope Index(string boundary, string worktree, string content, string path = "src/Invoice.cs", string? extra = null)
    {
        _registry.Authorize(boundary, "repo", "main", worktree);
        var manifest = ContentManifest.Create([new(path, FileHash.Create(System.Text.Encoding.UTF8.GetBytes(content)))]);
        var scope = _resolver.ResolveForIndexing(new ScopeRequest(boundary, "repo", "main", worktree), manifest, Commit);
        _registry.Publish(scope.Scope);
        _store.Add(new StoredOccurrence(scope.Scope, path, "Invoice", content, [1, 0]));
        if (extra is not null) _store.Add(new StoredOccurrence(scope.Scope, "docs/Invoice.md", "InvoiceDoc", extra, [0, 1]));
        return _resolver.ResolveForQuery(new ScopeRequest(boundary, "repo", "main", worktree));
    }

    private static IEnumerable<IReadOnlyList<StoredOccurrence>> AllArms(ScopedTestStore s, ResolvedScope scope) =>
    [
        s.Exact(scope, "Invoice"),
        s.Lexical(scope, "Invoice"),
        s.Vector(scope, [1, 0]),
        s.Graph(scope, "Invoice"),
        s.DocumentationFor(scope, "Invoice"),
        s.FileLookup(scope, "src/Invoice.cs") is { } hit ? [hit] : [],
    ];

    [Fact]
    public void Identical_content_in_two_scopes_stays_two_occurrences_and_no_arm_crosses_over()
    {
        var a = Index("team-a", "wt-1", Shared, extra: "Invoice docs of A");
        var b = Index("team-b", "wt-1", Shared, extra: "Invoice docs of B");

        foreach (var (scope, other) in new[] { (a, b), (b, a) })
        {
            foreach (var results in AllArms(_store, scope))
            {
                Assert.All(results, o => scope.RequireContains(o.Scope));
                Assert.DoesNotContain(results, o => other.Contains(o.Scope));
            }
        }
        Assert.NotEqual(
            OccurrenceKey.Create(LogicalKey.Create("repo", "src/Invoice.cs", "Invoice", "class", null), a.Scope),
            OccurrenceKey.Create(LogicalKey.Create("repo", "src/Invoice.cs", "Invoice", "class", null), b.Scope));
    }

    [Fact]
    public void Dirty_content_at_the_same_head_is_invisible_to_the_clean_worktree_and_the_reverse()
    {
        var clean = Index("team-a", "wt-clean", Shared);
        var dirty = Index("team-a", "wt-dirty", DirtySecret);

        Assert.NotEqual(clean.Scope.SnapshotId, dirty.Scope.SnapshotId);
        foreach (var results in AllArms(_store, clean))
            Assert.DoesNotContain(results, o => o.Text.Contains("UNPUBLISHED-DIRTY-EDIT", StringComparison.Ordinal));
        foreach (var results in AllArms(_store, dirty))
            Assert.DoesNotContain(results, o => o.Text.Contains("shared", StringComparison.Ordinal));
    }

    [Fact]
    public void An_older_snapshot_of_the_same_worktree_is_not_mixed_into_the_current_one()
    {
        var first = Index("team-a", "wt-1", "class Invoice { /* v1 */ }");
        var second = Index("team-a", "wt-1", "class Invoice { /* v2 */ }");

        Assert.NotEqual(first.Scope.SnapshotId, second.Scope.SnapshotId);
        Assert.Equal("class Invoice { /* v2 */ }", Assert.Single(_store.Lexical(second, "Invoice")).Text);
        Assert.Equal("class Invoice { /* v1 */ }", Assert.Single(_store.Lexical(first, "Invoice")).Text);
    }

    [Fact]
    public void The_scope_is_applied_before_candidate_retrieval_not_after_ranking()
    {
        Index("team-a", "wt-1", Shared);
        Index("team-b", "wt-1", Shared);
        Index("team-c", "wt-1", Shared);
        var scope = _resolver.ResolveForQuery(new ScopeRequest("team-a", "repo", "main", "wt-1"));

        _ = AllArms(_store, scope).ToList();

        // Six arms, one occurrence of team-a each (one file occurrence, no docs): foreign data was never examined.
        Assert.True(_store.Examined <= 6, $"Examined {_store.Examined} occurrences; foreign scopes were read.");
    }

    [Fact]
    public void An_unauthorized_request_never_reaches_the_store()
    {
        Index("team-a", "wt-1", Shared);

        Assert.Throws<Polifemo.Contracts.Errors.ScopeException>(
            () => _resolver.ResolveForQuery(new ScopeRequest("team-intruder", "repo", "main", "wt-1")));
        Assert.Equal(0, _store.Examined);
    }
}

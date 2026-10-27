using Polifemo.Contracts.Provenance;
using Polifemo.Contracts.Scope;

namespace Polifemo.Tests.Support;

public sealed record StoredOccurrence(SourceScope Scope, string Path, string Name, string Text, float[] Vector);

/// <summary>
/// A stand-in for the real store that fixes the contract every retrieval arm must keep (boundaries.md): each
/// operation takes a <see cref="ResolvedScope"/>, applies its filter terms while it selects candidates, and
/// <see cref="Examined"/> counts every occurrence an arm looked at, so a test can prove nothing foreign was
/// even read, not merely hidden afterwards.
/// </summary>
public sealed class ScopedTestStore
{
    private readonly List<StoredOccurrence> _all = [];

    public int Examined { get; private set; }

    public void Add(StoredOccurrence occurrence) => _all.Add(occurrence);

    /// <summary>The only way an arm obtains candidates: the scope filter is applied here, before scoring or fusion.</summary>
    private List<StoredOccurrence> Candidates(ResolvedScope scope)
    {
        var terms = scope.FilterTerms;
        var selected = _all.Where(o => Matches(o.Scope, terms)).ToList();
        Examined += selected.Count;
        return selected;
    }

    private static bool Matches(SourceScope s, IReadOnlyDictionary<string, string> t) =>
        s.AuthorizationBoundary == t[ResolvedScope.BoundaryField] && s.RepositoryId == t[ResolvedScope.RepositoryField]
        && s.BranchRef == t[ResolvedScope.BranchField] && s.WorktreeId == t[ResolvedScope.WorktreeField]
        && s.SnapshotId == t[ResolvedScope.SnapshotField];

    public IReadOnlyList<StoredOccurrence> Exact(ResolvedScope scope, string name) =>
        Candidates(scope).Where(o => o.Name == name).ToList();

    public IReadOnlyList<StoredOccurrence> Lexical(ResolvedScope scope, string term) =>
        Candidates(scope).Where(o => o.Text.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<StoredOccurrence> Vector(ResolvedScope scope, float[] query) =>
        Candidates(scope).OrderByDescending(o => o.Vector.Zip(query, (a, b) => a * b).Sum()).ToList();

    public IReadOnlyList<StoredOccurrence> Graph(ResolvedScope scope, string fromName) =>
        Candidates(scope).Where(o => o.Text.Contains(fromName, StringComparison.Ordinal) && o.Name != fromName).ToList();

    public StoredOccurrence? FileLookup(ResolvedScope scope, string path) =>
        Candidates(scope).FirstOrDefault(o => o.Path == path);

    public IReadOnlyList<StoredOccurrence> DocumentationFor(ResolvedScope scope, string name) =>
        Candidates(scope).Where(o => o.Path.EndsWith(".md", StringComparison.Ordinal) && o.Text.Contains(name, StringComparison.Ordinal)).ToList();
}

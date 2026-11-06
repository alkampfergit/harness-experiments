using System.Globalization;
using Polifemo.Contracts.Hashing;

namespace Polifemo.Platform.TokenSave;

/// <summary>Settings of one <see cref="TokenSaveAdapter.ReadAsync"/> call. The retry limit is fixed by the spec, not an option.</summary>
public sealed record SnapshotReadOptions
{
    /// <summary>How long SQLite waits for a writer's lock before the read fails with <c>TS_DB_LOCKED</c>.</summary>
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Pair the graph with the worktree bytes (research R5). Off only for callers that need the graph alone.</summary>
    public bool VerifySource { get; init; } = true;
}

/// <summary>A file-level documentation link as TokenSave records it: a <c>doc</c> node with a <c>documents</c> edge.</summary>
public sealed record DocAssociationRow(string DocPath, string DocNodeId, string TargetPath, string TargetNodeId)
{
    /// <summary><c>sidecar</c> for a <c>*.readme.md</c> next to the code, otherwise <c>central</c>.</summary>
    public string Kind => DocPath.EndsWith(".readme.md", StringComparison.OrdinalIgnoreCase) ? "sidecar" : "central";
}

/// <summary>Rows whose node ids are unknown. Informational: the schema cascades deletes, so these signal a TokenSave defect (research R3).</summary>
public sealed record IntegrityReport(int DanglingEdges, int DanglingUnresolved, int DanglingAmbiguousSources, int UnknownCandidateIds);

/// <summary>Where a snapshot came from. Carries no TokenSave timestamp (FR-010).</summary>
public sealed record TokenSaveProvenance(
    string DatabasePath, string Resolution, bool MetadataAbsent, string? IndexVersion, string? BinaryVersion,
    int Attempts, bool SourceVerified);

/// <summary>The rows a revision is computed from: the graph and its doc associations.</summary>
public sealed record GraphRows(TokenSaveGraph Graph, IReadOnlyList<DocAssociationRow> DocAssociations);

/// <summary>
/// Identity of the graph content (research R4): <c>Hashing.Hash("gr/v1", indexVersion, rows…)</c> over the canonical
/// rows of every table, sorted ordinal. Node ids are replaced by stable keys, because they differ across runs.
/// </summary>
public sealed record GraphRevision(string Value)
{
    private const char Sep = '\u001f';

    public static GraphRevision Compute(string indexVersion, GraphRows rows)
    {
        var graph = rows.Graph;
        var keys = graph.Symbols.ToDictionary(s => s.Id, s => Join(s.Kind, s.QualifiedName, s.FilePath, Num(s.StartLine)));
        string Key(string? id) => id is null ? "" : keys.TryGetValue(id, out var k) ? k : "?" + id;

        var canonical = new List<string>();
        canonical.AddRange(graph.Files.Select(f => Join("f", f.Path, f.ContentHash, f.Kind, Num(f.NodeCount))));
        canonical.AddRange(graph.Symbols.Select(s => Join("s", Key(s.Id), s.Name, Num(s.EndLine), s.Signature ?? "", s.Docstring ?? "", Key(s.ParentId))));
        canonical.AddRange(graph.Edges.Select(e => Join("e", Key(e.SourceId), Key(e.TargetId), e.Kind, Num(e.Line), Num(e.ResolvedBy))));
        canonical.AddRange(graph.Unresolved.Select(u => Join("u", Key(u.FromNodeId), u.ReferenceName, u.ReferenceKind, Num(u.Line), u.FilePath)));
        canonical.AddRange(graph.Ambiguous.Select(a => Join("a", Key(a.FromNodeId), a.ReferenceName, a.FilePath, Num(a.Line),
            string.Join(',', a.CandidateIds.Select(Key).Order(StringComparer.Ordinal)))));
        canonical.AddRange(rows.DocAssociations.Select(d => Join("d", d.DocPath, Key(d.DocNodeId), d.TargetPath, Key(d.TargetNodeId), d.Kind)));
        canonical.Sort(StringComparer.Ordinal);

        return new GraphRevision(Hashing.Hash("gr/v1", [indexVersion, .. canonical.Select(c => (HashField)c)]));
    }

    private static string Join(params string[] parts) => string.Join(Sep, parts);

    private static string Num(int? n) => n?.ToString(CultureInfo.InvariantCulture) ?? "";
}

/// <summary>Identity of the source inputs only: <c>Hashing.Hash("sf/v1", indexVersion, path₁, hash₁, …)</c> over <c>files</c> sorted by path.</summary>
public sealed record SourceFingerprint(string Value)
{
    public static SourceFingerprint Compute(string indexVersion, IReadOnlyList<FileRow> files) =>
        new(Hashing.Hash("sf/v1", [indexVersion, .. files
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .SelectMany(f => new HashField[] { f.Path, f.ContentHash })]));
}

/// <summary>
/// One consistent revision of a TokenSave graph (FR-008), constructed only after every check, read and
/// verification succeeded. It has no date or time member (FR-010); node ids and lines are provenance only.
/// </summary>
public sealed class GraphSnapshot
{
    public required GraphRevision Revision { get; init; }
    public required SourceFingerprint Source { get; init; }
    public required TokenSaveGraph Graph { get; init; }
    public required IReadOnlyList<DocAssociationRow> DocAssociations { get; init; }
    public required IntegrityReport Integrity { get; init; }
    public required TokenSaveProvenance Provenance { get; init; }
}

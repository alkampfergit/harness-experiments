using Microsoft.Data.Sqlite;
using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.TokenSave;

/// <summary>
/// The only way to obtain TokenSave graph rows (FR-001, contracts/tokensave-adapter.md). It resolves the active
/// database, checks it against the pin and reads every table inside one read-only transaction ended by
/// <c>ROLLBACK</c>. It never writes to <c>.tokensave/</c> and never reads a TokenSave timestamp.
/// </summary>
public sealed class TokenSaveAdapter
{
    /// <summary>Retries after the first attempt when the database or the source changed (approved Clarify answer B).</summary>
    public const int MaxRetries = 3;

    private readonly CompatibilityChecker _checker;
    private readonly Func<int, CancellationToken, Task>? _afterRowsRead;

    public TokenSaveAdapter() : this(new CompatibilityChecker()) { }

    public TokenSaveAdapter(CompatibilityChecker checker) : this(checker, null) { }

    /// <summary>Test seam: <paramref name="afterRowsRead"/> runs after the rows of an attempt were read (attempt number, 1-based).</summary>
    internal TokenSaveAdapter(CompatibilityChecker checker, Func<int, CancellationToken, Task>? afterRowsRead)
    {
        _checker = checker;
        _afterRowsRead = afterRowsRead;
    }

    public async Task<GraphSnapshot> ReadAsync(string worktree, SnapshotReadOptions? options = null, CancellationToken ct = default)
    {
        options ??= new SnapshotReadOptions();
        const int attempt = 1;
        var db = await _checker.Resolver.ResolveAsync(worktree, ct);
        var rows = await ReadRowsAsync(worktree, db, options, attempt, ct);
        if (_afterRowsRead is not null) await _afterRowsRead(attempt, ct);
        return Build(worktree, db, rows, attempt, sourceVerified: false);
    }

    private sealed record AttemptRows(
        TokenSaveGraph Graph, IReadOnlyList<DocAssociationRow> Docs, string? IndexVersion, string? BinaryVersion);

    private async Task<AttemptRows> ReadRowsAsync(
        string worktree, ResolvedDatabase db, SnapshotReadOptions options, int attempt, CancellationToken ct)
    {
        var relative = Relative(worktree, db.DatabasePath);
        try
        {
            using var reader = TokenSaveReader.OpenReadOnly(db.DatabasePath, options.LockTimeout);
            reader.BeginSnapshot();
            var report = await _checker.CheckOpenAsync(worktree, PlatformPins.TokenSave, db, reader, ct);
            if (!report.Ok) throw new TokenSaveCompatibilityException(report.Mismatches);

            var graph = new TokenSaveGraph
            {
                Files = reader.Files(),
                Symbols = reader.Symbols(),
                Edges = reader.Edges(),
                Unresolved = reader.Unresolved(),
                Ambiguous = reader.Ambiguous(),
                DatabasePath = db.DatabasePath,
            };
            var docs = reader.DocAssociations();
            reader.EndSnapshot();
            return new AttemptRows(graph, docs, report.IndexVersion, report.TokensaveBinaryVersion);
        }
        catch (SqliteException ex) when (TokenSaveReader.MapSqlite(ex, relative, options.LockTimeout, attempt) is { } mapped)
        {
            throw mapped;
        }
        catch (TokenSaveSnapshotException ex) when (ex.Attempts != attempt)
        {
            throw new TokenSaveSnapshotException(ex.Code, ex.Message, attempt);
        }
    }

    private static GraphSnapshot Build(string worktree, ResolvedDatabase db, AttemptRows rows, int attempt, bool sourceVerified)
    {
        var indexVersion = rows.IndexVersion ?? "";
        return new GraphSnapshot
        {
            Revision = GraphRevision.Compute(indexVersion, new GraphRows(rows.Graph, rows.Docs)),
            Source = SourceFingerprint.Compute(indexVersion, rows.Graph.Files),
            Graph = rows.Graph,
            DocAssociations = rows.Docs,
            Integrity = Integrity(rows.Graph),
            Provenance = new TokenSaveProvenance(
                Relative(worktree, db.DatabasePath), db.Resolution, db.MetadataAbsent, rows.IndexVersion, rows.BinaryVersion,
                attempt, sourceVerified),
        };
    }

    private static IntegrityReport Integrity(TokenSaveGraph graph)
    {
        var ids = graph.Symbols.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        return new IntegrityReport(
            graph.Edges.Count(e => !ids.Contains(e.SourceId) || !ids.Contains(e.TargetId)),
            graph.Unresolved.Count(u => !ids.Contains(u.FromNodeId)),
            graph.Ambiguous.Count(a => !ids.Contains(a.FromNodeId)),
            graph.Ambiguous.Sum(a => a.CandidateIds.Count(c => !ids.Contains(c))));
    }

    private static string Relative(string worktree, string path) =>
        Path.GetRelativePath(worktree, path).Replace('\\', '/');
}

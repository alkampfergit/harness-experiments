using System.Text.Json;
using Microsoft.Data.Sqlite;
using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.TokenSave;

public sealed record ActualColumn(string Name, string Type, bool NotNull);

public sealed record FileSnapshot(bool Exists, long Length, long LastWriteTicks)
{
    public static FileSnapshot Of(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new FileSnapshot(true, info.Length, info.LastWriteTimeUtc.Ticks) : new FileSnapshot(false, 0, 0);
    }
}

/// <summary>
/// The single read-only door into a TokenSave database (Constitution V). It opens with
/// <c>Mode=ReadOnly</c>, never commits and never selects a TokenSave timestamp (FR-010).
/// </summary>
internal sealed class TokenSaveReader : IDisposable
{
    private const string BeginSql = "BEGIN";
    private const string TouchSql = "SELECT count(*) FROM sqlite_master";
    private const string RollbackSql = "ROLLBACK";
    private const string TablesSql = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
    private const string ColumnsSql = "SELECT name, type, \"notnull\" FROM pragma_table_info($t)";
    private const string MetadataSql = "SELECT value FROM metadata WHERE key = $k";
    private const string SymbolsSql =
        "SELECT id, kind, name, qualified_name, file_path, start_line, end_line, signature, docstring, parent_id FROM nodes ORDER BY file_path, start_line, id";
    private const string EdgesSql = "SELECT source, target, kind, line, resolved_by FROM edges ORDER BY id";
    private const string UnresolvedSql =
        "SELECT from_node_id, reference_name, reference_kind, line, file_path FROM unresolved_refs ORDER BY file_path, line, reference_name, from_node_id, reference_kind";
    private const string AmbiguousSql =
        "SELECT from_node_id, reference_name, file_path, line, candidate_node_ids FROM ambiguous_calls ORDER BY file_path, line, reference_name";
    private const string FilesSql = "SELECT path, content_hash, kind, node_count FROM files ORDER BY path";
    private const string DocAssociationsSql =
        "SELECT d.file_path, d.id, t.file_path, t.id FROM edges e " +
        "JOIN nodes d ON d.id = e.source AND d.kind = 'doc' JOIN nodes t ON t.id = e.target " +
        "WHERE e.kind = 'documents' ORDER BY d.file_path, t.file_path, d.id, t.id";

    /// <summary>Every SQL text the reader executes, so a test can prove no timestamp column is read.</summary>
    internal static IReadOnlyList<string> SqlStatements { get; } =
    [
        BeginSql, TouchSql, RollbackSql, TablesSql, ColumnsSql, MetadataSql, SymbolsSql, EdgesSql,
        UnresolvedSql, AmbiguousSql, FilesSql, DocAssociationsSql,
    ];

    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(5);

    private readonly SqliteConnection _connection;
    private bool _inSnapshot;

    private TokenSaveReader(SqliteConnection connection) => _connection = connection;

    public static TokenSaveReader OpenReadOnly(string databasePath) => OpenReadOnly(databasePath, DefaultLockTimeout);

    /// <summary>Opens read-only; SQLite's busy handler waits up to <paramref name="lockTimeout"/> (whole seconds, at least 1).</summary>
    public static TokenSaveReader OpenReadOnly(string databasePath, TimeSpan lockTimeout)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = Math.Max(1, (int)lockTimeout.TotalSeconds),
        }.ToString());
        try
        {
            connection.Open();
        }
        catch
        {
            connection.Dispose();
            throw;
        }
        return new TokenSaveReader(connection);
    }

    /// <summary>Starts a read transaction so every following query sees one consistent snapshot.</summary>
    public void BeginSnapshot()
    {
        Execute(BeginSql);
        _inSnapshot = true;
        using var touch = _connection.CreateCommand();
        touch.CommandText = TouchSql;
        touch.ExecuteScalar();
    }

    /// <summary>Ends the read transaction with <c>ROLLBACK</c>, never <c>COMMIT</c> (research R2).</summary>
    public void EndSnapshot()
    {
        if (!_inSnapshot) return;
        _inSnapshot = false;
        Execute(RollbackSql);
    }

    public IReadOnlySet<string> Tables()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = TablesSql;
        using var r = cmd.ExecuteReader();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (r.Read()) names.Add(r.GetString(0));
        return names;
    }

    public IReadOnlyList<ActualColumn> Columns(string table)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = ColumnsSql;
        cmd.Parameters.AddWithValue("$t", table);
        using var r = cmd.ExecuteReader();
        var cols = new List<ActualColumn>();
        while (r.Read()) cols.Add(new ActualColumn(r.GetString(0), r.GetString(1), r.GetInt64(2) != 0));
        return cols;
    }

    public string? Metadata(string key)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = MetadataSql;
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public IReadOnlyList<SymbolRow> Symbols() => Query(SymbolsSql, "nodes", [0, 1, 2, 3, 4, 5, 6],
        r => new SymbolRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
            r.GetInt32(5), r.GetInt32(6), Text(r, 7), Text(r, 8), Text(r, 9)));

    public IReadOnlyList<EdgeRow> Edges() => Query(EdgesSql, "edges", [0, 1, 2],
        r => new EdgeRow(r.GetString(0), r.GetString(1), r.GetString(2),
            r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt32(4)));

    public IReadOnlyList<UnresolvedRow> Unresolved() => Query(UnresolvedSql, "unresolved_refs", [0, 1, 2, 3, 4],
        r => new UnresolvedRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4)));

    public IReadOnlyList<AmbiguousRow> Ambiguous() => Query(AmbiguousSql, "ambiguous_calls", [0, 1, 2, 3, 4],
        r => new AmbiguousRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3),
            Candidates(r.GetString(4), r.GetString(0), r.GetInt32(3))));

    public IReadOnlyList<FileRow> Files() => Query(FilesSql, "files", [0, 1, 2, 3],
        r => new FileRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));

    /// <summary>File-level doc links: <c>doc</c> nodes joined through <c>documents</c> edges to their target's file.</summary>
    public IReadOnlyList<DocAssociationRow> DocAssociations() => Query(DocAssociationsSql, "nodes", [0, 1, 2, 3],
        r => new DocAssociationRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));

    /// <summary>
    /// Maps SQLite's lock and damage codes to actionable errors (FR-006). Other codes return <c>null</c>, so a
    /// <c>catch … when</c> filter lets them propagate unchanged. Messages carry no SQL text (FR-012).
    /// </summary>
    internal static TokenSaveSnapshotException? MapSqlite(SqliteException ex, string relativeDbPath, TimeSpan lockTimeout, int attempt) =>
        (ex.SqliteErrorCode & 0xFF) switch
        {
            5 or 6 => new TokenSaveSnapshotException("TS_DB_LOCKED",
                $"A writer held '{relativeDbPath}' for longer than the lock timeout ({lockTimeout.TotalSeconds:0.#} s); " +
                "wait for `tokensave sync` to finish and retry.", attempt),
            10 or 11 or 26 => new TokenSaveSnapshotException("TS_DB_CORRUPT",
                $"'{relativeDbPath}' is not a readable TokenSave database (SQLite error {ex.SqliteErrorCode}); " +
                "run `tokensave sync -f` to rebuild the index, or restore the worktree.", attempt),
            _ => null,
        };

    private static string[] Candidates(string json, string fromNodeId, int line)
    {
        try
        {
            var ids = JsonSerializer.Deserialize<string?[]>(json);
            if (ids is not null && Array.TrueForAll(ids, id => id is not null)) return ids!;
        }
        catch (JsonException)
        {
            // Reported below without the stored value.
        }
        throw new TokenSaveSnapshotException("TS_DATA_INCOMPLETE",
            $"ambiguous_calls row (from_node_id {fromNodeId}, line {line}): candidate_node_ids is not a JSON array of strings; " +
            "run `tokensave sync -f` to rebuild the index.", 1);
    }

    private List<T> Query<T>(string sql, string table, int[] required, Func<SqliteDataReader, T> map)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var rows = new List<T>();
        while (r.Read())
        {
            foreach (var i in required.Where(r.IsDBNull))
                throw new TokenSaveSnapshotException("TS_DATA_INCOMPLETE",
                    $"{table} row {rows.Count + 1}: column {r.GetName(i)} is NULL but required; run `tokensave sync -f` to rebuild the index.", 1);
            rows.Add(map(r));
        }
        return rows;
    }

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    /// <summary>Test seam: proves the connection rejects writes.</summary>
    public void ExecuteForTest(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (_inSnapshot)
        {
            _inSnapshot = false;
            try { Execute(RollbackSql); }
            catch (SqliteException) { /* Disposing the connection rolls back as well. */ }
        }
        _connection.Dispose();
    }
}

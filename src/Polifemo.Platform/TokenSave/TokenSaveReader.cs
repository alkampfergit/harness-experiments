using Microsoft.Data.Sqlite;

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
/// <c>Mode=ReadOnly</c> and exposes only schema and metadata queries.
/// </summary>
internal sealed class TokenSaveReader : IDisposable
{
    private readonly SqliteConnection _connection;

    private TokenSaveReader(SqliteConnection connection) => _connection = connection;

    public static TokenSaveReader OpenReadOnly(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return new TokenSaveReader(connection);
    }

    /// <summary>Starts a read transaction so every following query sees one consistent snapshot.</summary>
    public void BeginSnapshot()
    {
        using var begin = _connection.CreateCommand();
        begin.CommandText = "BEGIN";
        begin.ExecuteNonQuery();
        using var touch = _connection.CreateCommand();
        touch.CommandText = "SELECT count(*) FROM sqlite_master";
        touch.ExecuteScalar();
    }

    public IReadOnlySet<string> Tables()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
        using var r = cmd.ExecuteReader();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (r.Read()) names.Add(r.GetString(0));
        return names;
    }

    public IReadOnlyList<ActualColumn> Columns(string table)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name, type, \"notnull\" FROM pragma_table_info($t)";
        cmd.Parameters.AddWithValue("$t", table);
        using var r = cmd.ExecuteReader();
        var cols = new List<ActualColumn>();
        while (r.Read()) cols.Add(new ActualColumn(r.GetString(0), r.GetString(1), r.GetInt64(2) != 0));
        return cols;
    }

    public string? Metadata(string key)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM metadata WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public IReadOnlyList<SymbolRow> Symbols() => Query(
        "SELECT id, kind, name, qualified_name, file_path, start_line, end_line, signature, docstring, parent_id FROM nodes ORDER BY file_path, start_line, id",
        r => new SymbolRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
            r.GetInt32(5), r.GetInt32(6), Text(r, 7), Text(r, 8), Text(r, 9)));

    public IReadOnlyList<EdgeRow> Edges() => Query(
        "SELECT source, target, kind, line, resolved_by FROM edges ORDER BY id",
        r => new EdgeRow(r.GetString(0), r.GetString(1), r.GetString(2),
            r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt32(4)));

    public IReadOnlyList<UnresolvedRow> Unresolved() => Query(
        "SELECT from_node_id, reference_name, reference_kind, line, file_path FROM unresolved_refs ORDER BY file_path, line, reference_name, from_node_id, reference_kind",
        r => new UnresolvedRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4)));

    public IReadOnlyList<AmbiguousRow> Ambiguous() => Query(
        "SELECT from_node_id, reference_name, file_path, line, candidate_node_ids FROM ambiguous_calls ORDER BY file_path, line, reference_name",
        r => new AmbiguousRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3),
            System.Text.Json.JsonSerializer.Deserialize<string[]>(r.GetString(4)) ?? []));

    public IReadOnlyList<FileRow> Files() => Query(
        "SELECT path, content_hash, kind, node_count FROM files ORDER BY path",
        r => new FileRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var rows = new List<T>();
        while (r.Read()) rows.Add(map(r));
        return rows;
    }

    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    /// <summary>Test seam: proves the connection rejects writes.</summary>
    public void ExecuteForTest(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}

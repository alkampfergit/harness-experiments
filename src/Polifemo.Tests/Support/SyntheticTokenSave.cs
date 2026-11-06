using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Polifemo.Platform.TokenSave;

namespace Polifemo.Tests.Support;

/// <summary>Builds a worktree with a synthesized .tokensave database, optionally with defects.</summary>
public sealed class SyntheticTokenSave
{
    private readonly Dictionary<string, List<ExpectedColumn>> _tables;
    public string IndexVersion { get; set; } = PlatformPins.TokenSave;
    public bool WriteIndexVersion { get; set; } = true;
    public List<string> ExtraTables { get; } = [];
    public string? BranchMetaJson { get; set; }
    public string DbFile { get; set; } = "tokensave.db";
    public bool CreateDbFile { get; set; } = true;
    /// <summary>Mimics a real TokenSave index, which is left in WAL journal mode with no -wal/-shm files.</summary>
    public bool WalMode { get; set; }

    private readonly List<(string Table, Dictionary<string, object?> Values)> _rows = [];

    public SyntheticTokenSave()
    {
        var schema = ExpectedSchema.Load();
        _tables = schema.Tables.ToDictionary(t => t.Key, t => t.Value.ToList());
    }

    public SyntheticTokenSave DropTable(string table) { _tables.Remove(table); return this; }

    public SyntheticTokenSave DropColumn(string table, string column)
    {
        _tables[table].RemoveAll(c => c.Name == column);
        return this;
    }

    public SyntheticTokenSave ChangeColumn(string table, string column, string? type = null, bool? notNull = null)
    {
        var list = _tables[table];
        var i = list.FindIndex(c => c.Name == column);
        list[i] = list[i] with { Type = type ?? list[i].Type, NotNull = notNull ?? list[i].NotNull };
        return this;
    }

    public SyntheticTokenSave AddColumn(string table, string column)
    {
        _tables[table].Add(new ExpectedColumn(column, "TEXT", false, false));
        return this;
    }

    public SyntheticTokenSave AddFile(string path, string contentHash, string kind = "code", int nodeCount = 0) =>
        Row("files", new() { ["path"] = path, ["content_hash"] = contentHash, ["kind"] = kind, ["node_count"] = nodeCount });

    public SyntheticTokenSave AddNode(
        string id, string kind, string name, string qualifiedName, string filePath, int startLine, int endLine,
        string? signature = null, string? docstring = null, string? parentId = null) =>
        Row("nodes", new()
        {
            ["id"] = id, ["kind"] = kind, ["name"] = name, ["qualified_name"] = qualifiedName, ["file_path"] = filePath,
            ["start_line"] = startLine, ["end_line"] = endLine, ["signature"] = signature, ["docstring"] = docstring,
            ["parent_id"] = parentId, ["visibility"] = "public",
        });

    public SyntheticTokenSave AddEdge(string sourceId, string targetId, string kind, int? line = null, int? resolvedBy = null) =>
        Row("edges", new() { ["source"] = sourceId, ["target"] = targetId, ["kind"] = kind, ["line"] = line, ["resolved_by"] = resolvedBy });

    public SyntheticTokenSave AddUnresolved(string fromNodeId, string referenceName, string referenceKind, int line, string filePath) =>
        Row("unresolved_refs", new()
        {
            ["from_node_id"] = fromNodeId, ["reference_name"] = referenceName, ["reference_kind"] = referenceKind,
            ["line"] = line, ["file_path"] = filePath,
        });

    /// <summary><paramref name="candidateIdsJson"/> is stored as given, so a test can store an invalid value.</summary>
    public SyntheticTokenSave AddAmbiguous(string fromNodeId, string referenceName, string filePath, int line, string candidateIdsJson) =>
        Row("ambiguous_calls", new()
        {
            ["from_node_id"] = fromNodeId, ["reference_name"] = referenceName, ["file_path"] = filePath,
            ["line"] = line, ["candidate_node_ids"] = candidateIdsJson,
        });

    /// <summary>A <c>doc</c> node (once per id) and a <c>documents</c> edge to the target, which gets a <c>file</c> node when it has none.</summary>
    public SyntheticTokenSave AddDoc(string docPath, string docNodeId, string targetPath, string targetNodeId)
    {
        if (!HasNode(docNodeId)) AddNode(docNodeId, "doc", Path.GetFileName(docPath), docPath, docPath, 0, 0);
        if (!HasNode(targetNodeId)) AddNode(targetNodeId, "file", Path.GetFileName(targetPath), targetPath, targetPath, 0, 0);
        return AddEdge(docNodeId, targetNodeId, "documents");
    }

    private bool HasNode(string id) => _rows.Exists(r => r.Table == "nodes" && Equals(r.Values["id"], id));

    private SyntheticTokenSave Row(string table, Dictionary<string, object?> values)
    {
        _rows.Add((table, values));
        return this;
    }

    /// <summary>Writes worktree files (relative paths with <c>/</c>) so their hashes can match the <c>files</c> rows.</summary>
    public static void WriteSourceFiles(string root, IReadOnlyDictionary<string, byte[]> files)
    {
        foreach (var (rel, bytes) in files)
        {
            var full = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
    }

    /// <summary>The <c>files.content_hash</c> convention: lowercase hex SHA-256 of the raw bytes.</summary>
    public static string ContentHashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Overwrites the first 100 bytes (the SQLite header) so the file is no longer a database.</summary>
    public static void CorruptDatabase(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
        stream.Write(Enumerable.Repeat((byte)'x', 100).ToArray());
    }

    /// <summary>
    /// Holds <c>BEGIN EXCLUSIVE</c> on a separate read-write connection until disposed. Use a rollback-journal
    /// database: <c>BEGIN IMMEDIATE</c> (a RESERVED lock) still lets readers in, and in WAL mode no writer blocks them.
    /// </summary>
    public static IDisposable HoldWriteLock(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        conn.Open();
        Exec(conn, "BEGIN EXCLUSIVE");
        return new WriteLock(conn);
    }

    private sealed class WriteLock(SqliteConnection conn) : IDisposable
    {
        public void Dispose()
        {
            Exec(conn, "ROLLBACK");
            conn.Dispose();
        }
    }

    /// <summary>Creates the worktree directory and returns its path.</summary>
    public string Build(string root)
    {
        var ts = Path.Combine(root, ".tokensave");
        Directory.CreateDirectory(ts);
        if (BranchMetaJson is not null)
            File.WriteAllText(Path.Combine(ts, "branch-meta.json"), BranchMetaJson);
        if (!CreateDbFile) return root;

        var dbPath = Path.Combine(ts, DbFile == "" ? "tokensave.db" : DbFile);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString()))
        {
            conn.Open();
            if (WalMode) Exec(conn, "PRAGMA journal_mode=WAL");
            foreach (var (table, cols) in _tables)
                Exec(conn, CreateTableSql(table, cols));
            foreach (var extra in ExtraTables)
                Exec(conn, $"CREATE TABLE \"{extra}\" (x TEXT)");
            if (WriteIndexVersion && _tables.ContainsKey("metadata"))
                Exec(conn, $"INSERT INTO metadata(key, value) VALUES ('last_full_index_version', '{IndexVersion}')");
            foreach (var (table, values) in _rows)
                Insert(conn, table, _tables[table], values);
        }
        return root;
    }

    public static string BranchMeta(string defaultBranch, params (string Branch, string DbFile)[] branches) =>
        "{\"default_branch\":\"" + defaultBranch + "\",\"branches\":{" +
        string.Join(",", branches.Select(b => $"\"{b.Branch}\":{{\"db_file\":\"{b.DbFile}\"}}")) + "}}";

    private static string CreateTableSql(string table, List<ExpectedColumn> cols)
    {
        var keys = cols.Where(c => c.PrimaryKey).ToList();
        var parts = cols.Select(c => $"\"{c.Name}\" {c.Type}" + (keys.Count == 1 && c.PrimaryKey ? " PRIMARY KEY" : "") + (c.NotNull ? " NOT NULL" : "")).ToList();
        if (keys.Count > 1) parts.Add($"PRIMARY KEY ({string.Join(", ", keys.Select(k => $"\"{k.Name}\""))})");
        return $"CREATE TABLE \"{table}\" ({string.Join(", ", parts)})";
    }

    /// <summary>Inserts the given values; NOT NULL columns the row does not name get 0 or an empty string.</summary>
    private static void Insert(SqliteConnection conn, string table, List<ExpectedColumn> cols, Dictionary<string, object?> values)
    {
        var used = cols.Where(c => values.ContainsKey(c.Name) || c.NotNull).ToList();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO \"{table}\" ({string.Join(", ", used.Select(c => $"\"{c.Name}\""))}) " +
                          $"VALUES ({string.Join(", ", used.Select((_, i) => $"$p{i}"))})";
        for (var i = 0; i < used.Count; i++)
        {
            var c = used[i];
            var value = values.TryGetValue(c.Name, out var v) ? v : c.Type.Equals("TEXT", StringComparison.OrdinalIgnoreCase) ? "" : 0;
            cmd.Parameters.AddWithValue($"$p{i}", value ?? DBNull.Value);
        }
        cmd.ExecuteNonQuery();
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

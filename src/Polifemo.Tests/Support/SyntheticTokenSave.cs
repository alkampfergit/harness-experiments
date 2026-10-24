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
        using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            conn.Open();
            if (WalMode) Exec(conn, "PRAGMA journal_mode=WAL");
            foreach (var (table, cols) in _tables)
                Exec(conn, CreateTableSql(table, cols));
            foreach (var extra in ExtraTables)
                Exec(conn, $"CREATE TABLE \"{extra}\" (x TEXT)");
            if (WriteIndexVersion && _tables.ContainsKey("metadata"))
                Exec(conn, $"INSERT INTO metadata(key, value) VALUES ('last_full_index_version', '{IndexVersion}')");
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

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

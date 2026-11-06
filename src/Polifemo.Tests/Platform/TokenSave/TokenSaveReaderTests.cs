using Microsoft.Data.Sqlite;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.TokenSave;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.TokenSave;

/// <summary>White-box tests of the reader's row mapping and error mapping (F03-01 T010, T011).</summary>
public sealed class TokenSaveReaderTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private string Db => Path.Combine(_ws.Path, ".tokensave", "tokensave.db");

    [Fact]
    public void Doc_associations_join_doc_nodes_through_documents_edges()
    {
        new SyntheticTokenSave()
            .AddNode("c1", "class", "A", "src/A.cs::A", "src/A.cs", 0, 3)
            .AddDoc("src/A.readme.md", "d1", "src/A.cs", "c1")
            .AddDoc("tokensave-docs/guide.md", "d2", "src/B.cs", "f2")
            .AddEdge("c1", "f2", "calls")
            .Build(_ws.Path);

        using var reader = TokenSaveReader.OpenReadOnly(Db);
        var docs = reader.DocAssociations();

        Assert.Equal(
            [new DocAssociationRow("src/A.readme.md", "d1", "src/A.cs", "c1"), new DocAssociationRow("tokensave-docs/guide.md", "d2", "src/B.cs", "f2")],
            docs);
    }

    [Fact]
    public void Invalid_candidate_json_is_TS_DATA_INCOMPLETE_without_the_value()
    {
        new SyntheticTokenSave().AddAmbiguous("n1", "Secret", "src/A.cs", 7, "{\"SENTINEL\":1}").Build(_ws.Path);

        using var reader = TokenSaveReader.OpenReadOnly(Db);
        var ex = Assert.Throws<TokenSaveSnapshotException>(() => reader.Ambiguous());

        Assert.Equal("TS_DATA_INCOMPLETE", ex.Code);
        Assert.Contains("ambiguous_calls", ex.Message);
        Assert.Contains("n1", ex.Message);
        Assert.Contains("line 7", ex.Message);
        Assert.DoesNotContain("SENTINEL", ex.Message);
        Assert.DoesNotContain("Secret", ex.Message);
    }

    [Fact]
    public void A_null_string_inside_the_candidate_list_is_TS_DATA_INCOMPLETE()
    {
        new SyntheticTokenSave().AddAmbiguous("n1", "X", "src/A.cs", 1, "[\"a\", null]").Build(_ws.Path);
        using var reader = TokenSaveReader.OpenReadOnly(Db);
        Assert.Equal("TS_DATA_INCOMPLETE", Assert.Throws<TokenSaveSnapshotException>(() => reader.Ambiguous()).Code);
    }

    [Fact]
    public void Null_in_a_required_column_is_TS_DATA_INCOMPLETE_naming_table_and_column()
    {
        // The compatibility check rejects a nullable column first; the reader still refuses to map a NULL.
        new SyntheticTokenSave().ChangeColumn("files", "kind", notNull: false).AddFile("src/A.cs", "aa").Build(_ws.Path);
        using (var conn = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE files SET kind = NULL";
            cmd.ExecuteNonQuery();
        }

        using var reader = TokenSaveReader.OpenReadOnly(Db);
        var ex = Assert.Throws<TokenSaveSnapshotException>(() => reader.Files());

        Assert.Equal("TS_DATA_INCOMPLETE", ex.Code);
        Assert.Contains("files row 1", ex.Message);
        Assert.Contains("kind", ex.Message);
    }

    [Fact]
    public void End_snapshot_rolls_back_and_is_idempotent()
    {
        new SyntheticTokenSave().Build(_ws.Path);
        using var reader = TokenSaveReader.OpenReadOnly(Db, TimeSpan.FromSeconds(2));
        reader.BeginSnapshot();
        reader.EndSnapshot();
        reader.EndSnapshot();

        // A second transaction can start only because the first one ended.
        Assert.Null(Record.Exception(reader.BeginSnapshot));
    }

    [Fact]
    public void Sql_statements_list_every_query()
    {
        Assert.Contains(TokenSaveReader.SqlStatements, s => s.Contains("FROM ambiguous_calls", StringComparison.Ordinal));
        Assert.Contains("ROLLBACK", TokenSaveReader.SqlStatements);
        Assert.DoesNotContain("COMMIT", TokenSaveReader.SqlStatements);
    }

    [Theory]
    [InlineData(5, "TS_DB_LOCKED", "wait for `tokensave sync` to finish")]
    [InlineData(6, "TS_DB_LOCKED", "wait for `tokensave sync` to finish")]
    [InlineData(10, "TS_DB_CORRUPT", "`tokensave sync -f`")]
    [InlineData(11, "TS_DB_CORRUPT", "`tokensave sync -f`")]
    [InlineData(26, "TS_DB_CORRUPT", "`tokensave sync -f`")]
    [InlineData(266, "TS_DB_CORRUPT", "`tokensave sync -f`")] // SQLITE_IOERR_READ, an extended code
    public void Lock_and_damage_codes_are_mapped(int sqliteCode, string code, string action)
    {
        var mapped = TokenSaveReader.MapSqlite(new SqliteException("SELECT secret", sqliteCode), ".tokensave/tokensave.db", TimeSpan.FromSeconds(3), 2);

        Assert.NotNull(mapped);
        Assert.Equal(code, mapped.Code);
        Assert.Equal(2, mapped.Attempts);
        Assert.Contains(action, mapped.Message);
        Assert.Contains(".tokensave/tokensave.db", mapped.Message);
        Assert.DoesNotContain("SELECT", mapped.Message);
    }

    [Fact]
    public void Other_sqlite_codes_are_not_mapped()
    {
        Assert.Null(TokenSaveReader.MapSqlite(new SqliteException("x", 1), "db", TimeSpan.FromSeconds(1), 1));
    }
}

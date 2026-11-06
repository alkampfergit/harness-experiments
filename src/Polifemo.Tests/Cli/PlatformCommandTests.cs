using System.Text.Json;
using Polifemo.Cli;
using Polifemo.Platform.TokenSave;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Cli;

public sealed class PlatformCommandTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private static async Task<(int Exit, string Out, string Err)> Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await CliApp.RunAsync(args, stdout, stderr, binaryVersion: _ => Task.FromResult(PlatformPins.TokenSave));
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task Check_success_prints_the_contract_shape_and_exits_0()
    {
        new SyntheticTokenSave().Build(_ws.Path);

        var (exit, stdout, _) = await Run("platform", "check", "--worktree", _ws.Path);

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        foreach (var p in new[] { "tokensaveBinaryVersion", "indexVersion", "databasePath", "resolution", "mismatches", "warnings" })
            Assert.True(root.TryGetProperty(p, out _), p);
    }

    [Fact]
    public async Task Check_mismatch_exits_1_with_named_code()
    {
        new SyntheticTokenSave().DropTable("nodes").Build(_ws.Path);

        var (exit, stdout, _) = await Run("platform", "check", "--worktree", _ws.Path);

        Assert.Equal(1, exit);
        var m = JsonDocument.Parse(stdout).RootElement.GetProperty("mismatches")[0];
        Assert.Equal("TS_TABLE_MISSING", m.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Expect_tokensave_flag_overrides_the_pin()
    {
        new SyntheticTokenSave().Build(_ws.Path);
        var (exit, stdout, _) = await Run("platform", "check", "--worktree", _ws.Path, "--expect-tokensave", "0.0.0");
        Assert.Equal(1, exit);
        Assert.Contains("TS_VERSION_MISMATCH", stdout);
    }

    [Fact]
    public async Task Missing_required_argument_exits_2()
    {
        var (exit, _, _) = await Run("platform", "check");
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Unreachable_store_exits_3()
    {
        var url = $"http://127.0.0.1:{FakeElasticsearch.FreePort()}";
        var (exit, _, stderr) = await Run("platform", "smoke", "--url", url);
        Assert.Equal(3, exit);
        Assert.Contains("ES_UNREACHABLE", stderr);
    }

    [Fact]
    public async Task Smoke_never_accepts_a_password_argument()
    {
        var (exit, _, _) = await Run("platform", "smoke", "--password", "x");
        Assert.Equal(2, exit);
    }

    private const string Secret = "SECRET_SYMBOL_TEXT";

    private void SnapshotFixture(SyntheticTokenSave? ts = null)
    {
        var body = "class Secret {}\n"u8.ToArray();
        SyntheticTokenSave.WriteSourceFiles(_ws.Path, new Dictionary<string, byte[]> { ["src/A.cs"] = body });
        (ts ?? new SyntheticTokenSave())
            .AddFile("src/A.cs", SyntheticTokenSave.ContentHashOf(body), nodeCount: 1)
            .AddNode("n1", "class", Secret, "src/A.cs::" + Secret, "src/A.cs", 0, 0, signature: Secret, docstring: Secret)
            .AddDoc("src/A.readme.md", "d1", "src/A.cs", "n1")
            .Build(_ws.Path);
    }

    [Fact]
    public async Task Snapshot_success_prints_counts_revision_and_provenance_without_row_content()
    {
        SnapshotFixture();

        var (exit, stdout, stderr) = await Run("platform", "snapshot", "--worktree", _ws.Path);

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("revision").GetString());
        var counts = root.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("files").GetInt32());
        Assert.Equal(2, counts.GetProperty("symbols").GetInt32());
        Assert.Equal(1, counts.GetProperty("edges").GetInt32());
        Assert.Equal(0, counts.GetProperty("unresolved").GetInt32());
        Assert.Equal(0, counts.GetProperty("ambiguous").GetInt32());
        Assert.Equal(1, counts.GetProperty("docAssociations").GetInt32());
        Assert.True(root.TryGetProperty("integrity", out _));
        Assert.True(root.TryGetProperty("provenance", out _));
        var ts = root.GetProperty("tokensave");
        foreach (var p in new[] { "databasePath", "resolution", "metadataAbsent", "indexVersion", "binaryVersion", "attempts", "sourceVerified" })
            Assert.True(ts.TryGetProperty(p, out _), p);
        Assert.Equal(".tokensave/tokensave.db", ts.GetProperty("databasePath").GetString());
        Assert.DoesNotContain(Secret, stdout + stderr);
    }

    [Fact]
    public async Task Snapshot_accepts_no_verify_source_and_lock_timeout()
    {
        SnapshotFixture();

        var (exit, stdout, _) = await Run("platform", "snapshot", "--worktree", _ws.Path, "--no-verify-source", "--lock-timeout", "1");

        Assert.Equal(0, exit);
        Assert.False(JsonDocument.Parse(stdout).RootElement.GetProperty("tokensave").GetProperty("sourceVerified").GetBoolean());
    }

    [Fact]
    public async Task Snapshot_with_malformed_metadata_exits_1_with_TS_META_MALFORMED()
    {
        SnapshotFixture(new SyntheticTokenSave { BranchMetaJson = "{ nope" });

        var (exit, stdout, stderr) = await Run("platform", "snapshot", "--worktree", _ws.Path);

        Assert.Equal(1, exit);
        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("TS_META_MALFORMED", root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("message").GetString()));
        Assert.DoesNotContain(Secret, stdout + stderr);
    }

    [Fact]
    public async Task Snapshot_of_a_corrupt_database_exits_1_with_TS_DB_CORRUPT()
    {
        SnapshotFixture();
        SyntheticTokenSave.CorruptDatabase(Path.Combine(_ws.Path, ".tokensave", "tokensave.db"));

        var (exit, stdout, stderr) = await Run("platform", "snapshot", "--worktree", _ws.Path);

        Assert.Equal(1, exit);
        Assert.Equal("TS_DB_CORRUPT", JsonDocument.Parse(stdout).RootElement.GetProperty("code").GetString());
        Assert.StartsWith("TS_DB_CORRUPT: ", stderr);
    }

    [Fact]
    public async Task Snapshot_compatibility_failure_lists_every_mismatch()
    {
        SnapshotFixture(new SyntheticTokenSave().DropTable("edges"));

        var (exit, stdout, _) = await Run("platform", "snapshot", "--worktree", _ws.Path);

        Assert.Equal(1, exit);
        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal("TS_TABLE_MISSING", root.GetProperty("code").GetString());
        Assert.Equal("edges", root.GetProperty("mismatches")[0].GetProperty("subject").GetString());
    }

    [Theory]
    [InlineData]
    [InlineData("--worktree", "/nonexistent/worktree")]
    [InlineData("--worktree", ".", "--lock-timeout", "0")]
    public async Task Snapshot_usage_errors_exit_2(params string[] extra)
    {
        var (exit, _, _) = await Run(["platform", "snapshot", .. extra]);
        Assert.Equal(2, exit);
    }
}

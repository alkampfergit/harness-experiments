using Polifemo.Platform.TokenSave;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.TokenSave;

public sealed class CompatibilityCheckerTests : IDisposable
{
    private static readonly string[] ExpectedBrokenCodes =
        ["TS_COLUMN_CHANGED", "TS_COLUMN_MISSING", "TS_INDEX_VERSION_MISMATCH", "TS_TABLE_MISSING", "TS_VERSION_MISMATCH"];
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private static CompatibilityChecker Checker(string binaryVersion = PlatformPins.TokenSave) =>
        new(new ActiveDatabaseResolver((_, _) => Task.FromResult<string?>("main")), _ => Task.FromResult(binaryVersion));

    private Task<CompatibilityReport> Check(SyntheticTokenSave ts, string expect = PlatformPins.TokenSave, string binary = PlatformPins.TokenSave) =>
        Checker(binary).CheckAsync(ts.Build(_ws.Path), expect);

    private static IEnumerable<string> Codes(CompatibilityReport r) => r.Mismatches.Select(m => m.Code);

    [Fact]
    public async Task Pinned_configuration_passes()
    {
        var r = await Check(new SyntheticTokenSave());

        Assert.True(r.Ok);
        Assert.Empty(r.Mismatches);
        Assert.Equal("default", r.Resolution);
        Assert.Equal(PlatformPins.TokenSave, r.TokensaveBinaryVersion);
        Assert.Equal(PlatformPins.TokenSave, r.IndexVersion);
        Assert.Equal(Path.Combine(".tokensave", "tokensave.db"), r.DatabasePath);
    }

    [Theory]
    [InlineData("""{"report_savings": true}""", "TS_UPLOAD_ENABLED")]
    [InlineData("""{"report_savings": false}""", null)]
    public async Task Tokensave_upload_setting_is_checked(string config, string? code)
    {
        var worktree = new SyntheticTokenSave().Build(_ws.Path);
        File.WriteAllText(Path.Combine(worktree, ".tokensave", "config.json"), config);

        var r = await Checker().CheckAsync(worktree, PlatformPins.TokenSave, TestContext.Current.CancellationToken);

        Assert.Equal(code is null, r.Ok);
        Assert.Equal(code is null ? [] : [code], Codes(r));
        Assert.DoesNotContain(r.Warnings, w => w.Code == "TS_UPLOAD_SETTING_ABSENT");
    }

    [Fact]
    public async Task An_unstated_upload_setting_is_a_warning_not_a_failure()
    {
        var r = await Check(new SyntheticTokenSave());

        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Code == "TS_UPLOAD_SETTING_ABSENT");
    }

    [Fact]
    public async Task Wrong_binary_version_is_TS_VERSION_MISMATCH()
    {
        var r = await Check(new SyntheticTokenSave(), binary: "7.15.0");

        Assert.False(r.Ok);
        var m = Assert.Single(r.Mismatches);
        Assert.Equal("TS_VERSION_MISMATCH", m.Code);
        Assert.Equal(PlatformPins.TokenSave, m.Expected);
        Assert.Equal("7.15.0", m.Actual);
    }

    [Fact]
    public async Task Custom_expected_version_is_honoured()
    {
        var r = await Check(new SyntheticTokenSave(), expect: "0.0.0");
        Assert.Contains("TS_VERSION_MISMATCH", Codes(r));
        Assert.Contains("TS_INDEX_VERSION_MISMATCH", Codes(r));
    }

    [Fact]
    public async Task Stale_index_version_is_TS_INDEX_VERSION_MISMATCH()
    {
        var r = await Check(new SyntheticTokenSave { IndexVersion = "7.15.0" });

        var m = Assert.Single(r.Mismatches);
        Assert.Equal("TS_INDEX_VERSION_MISMATCH", m.Code);
        Assert.Equal("7.15.0", m.Actual);
        Assert.Equal("7.15.0", r.IndexVersion);
    }

    [Fact]
    public async Task Missing_index_version_is_TS_INDEX_VERSION_MISMATCH()
    {
        var r = await Check(new SyntheticTokenSave { WriteIndexVersion = false });
        Assert.Equal(["TS_INDEX_VERSION_MISMATCH"], Codes(r));
        Assert.Null(r.IndexVersion);
    }

    [Fact]
    public async Task Dropped_table_is_TS_TABLE_MISSING()
    {
        var r = await Check(new SyntheticTokenSave().DropTable("edges"));
        var m = Assert.Single(r.Mismatches);
        Assert.Equal("TS_TABLE_MISSING", m.Code);
        Assert.Equal("edges", m.Subject);
    }

    [Fact]
    public async Task Dropped_column_is_TS_COLUMN_MISSING()
    {
        var r = await Check(new SyntheticTokenSave().DropColumn("nodes", "qualified_name"));
        var m = Assert.Single(r.Mismatches);
        Assert.Equal("TS_COLUMN_MISSING", m.Code);
        Assert.Equal("nodes.qualified_name", m.Subject);
    }

    [Theory]
    [InlineData("INTEGER", null)]
    [InlineData(null, false)]
    public async Task Changed_type_or_not_null_is_TS_COLUMN_CHANGED(string? type, bool? notNull)
    {
        var r = await Check(new SyntheticTokenSave().ChangeColumn("nodes", "kind", type, notNull));
        var m = Assert.Single(r.Mismatches);
        Assert.Equal("TS_COLUMN_CHANGED", m.Code);
        Assert.Equal("nodes.kind", m.Subject);
    }

    [Fact]
    public async Task Extra_table_and_column_pass_with_named_warnings()
    {
        var r = await Check(new SyntheticTokenSave { ExtraTables = { "read_cache" } }.AddColumn("files", "extra_col"));

        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Code == "TS_EXTRA_TABLE" && w.Subject == "read_cache");
        Assert.Contains(r.Warnings, w => w.Code == "TS_EXTRA_COLUMN" && w.Subject == "files.extra_col");
    }

    [Fact]
    public async Task All_defects_are_reported_not_just_the_first()
    {
        var ts = new SyntheticTokenSave { IndexVersion = "7.15.0" }
            .DropTable("edges")
            .DropColumn("nodes", "name")
            .ChangeColumn("files", "size", type: "TEXT");

        var r = await Check(ts, binary: "7.14.0");

        Assert.False(r.Ok);
        Assert.Equal(
            ExpectedBrokenCodes,
            Codes(r).Distinct().Order().ToArray());
    }

    [Theory]
    [InlineData("{ nope", "TS_META_MALFORMED")]
    [InlineData("{\"default_branch\":\"main\",\"branches\":{\"main\":{\"db_file\":\"gone.db\"}}}", "TS_DB_MISSING")]
    [InlineData("{\"default_branch\":\"main\",\"branches\":{}}", "TS_META_NO_DB")]
    public async Task Resolution_failures_become_named_mismatches(string metaJson, string code)
    {
        var r = await Check(new SyntheticTokenSave { BranchMetaJson = metaJson, CreateDbFile = false });

        Assert.False(r.Ok);
        Assert.Equal([code], Codes(r));
    }
}

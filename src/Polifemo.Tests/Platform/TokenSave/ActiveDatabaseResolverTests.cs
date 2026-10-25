using Polifemo.Contracts.Errors;
using Polifemo.Platform.TokenSave;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.TokenSave;

public class ActiveDatabaseResolverTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private static ActiveDatabaseResolver Resolver(string? branch) =>
        new((_, _) => Task.FromResult(branch));

    [Fact]
    public async Task Branch_meta_names_db_for_current_branch()
    {
        var ts = new SyntheticTokenSave { BranchMetaJson = SyntheticTokenSave.BranchMeta("main", ("main", "tokensave.db"), ("feat", "feat.db")), DbFile = "feat.db" };
        ts.Build(_ws.Path);

        var r = await Resolver("feat").ResolveAsync(_ws.Path, ct: TestContext.Current.CancellationToken);

        Assert.Equal("branch-meta", r.Resolution);
        Assert.Equal("feat.db", Path.GetFileName(r.DatabasePath));
    }

    [Fact]
    public async Task Unknown_branch_uses_default_branch_entry()
    {
        new SyntheticTokenSave { BranchMetaJson = SyntheticTokenSave.BranchMeta("main", ("main", "tokensave.db")) }.Build(_ws.Path);

        var r = await Resolver("topic").ResolveAsync(_ws.Path, ct: TestContext.Current.CancellationToken);

        Assert.Equal("branch-meta", r.Resolution);
        Assert.Equal("tokensave.db", Path.GetFileName(r.DatabasePath));
    }

    [Fact]
    public async Task Absent_metadata_uses_documented_default()
    {
        new SyntheticTokenSave().Build(_ws.Path);

        var r = await Resolver("main").ResolveAsync(_ws.Path, ct: TestContext.Current.CancellationToken);

        Assert.Equal("default", r.Resolution);
        Assert.EndsWith(Path.Combine(".tokensave", "tokensave.db"), r.DatabasePath);
    }

    [Fact]
    public async Task Malformed_json_is_TS_META_MALFORMED()
    {
        new SyntheticTokenSave { BranchMetaJson = "{ not json" }.Build(_ws.Path);
        var ex = await Assert.ThrowsAsync<ActiveDatabaseResolutionException>(() => Resolver("main").ResolveAsync(_ws.Path, ct: TestContext.Current.CancellationToken));
        Assert.Equal("TS_META_MALFORMED", ex.Code);
    }

    [Theory]
    [InlineData("{\"default_branch\":\"main\",\"branches\":{\"main\":{}}}")]
    [InlineData("{\"default_branch\":\"main\",\"branches\":{}}")]
    [InlineData("{\"branches\":{\"other\":{\"db_file\":\"x.db\"}}}")]
    public async Task No_usable_entry_is_TS_META_NO_DB(string json)
    {
        new SyntheticTokenSave { BranchMetaJson = json, CreateDbFile = false }.Build(_ws.Path);
        var ex = await Assert.ThrowsAsync<ActiveDatabaseResolutionException>(() => Resolver("main").ResolveAsync(_ws.Path, ct: TestContext.Current.CancellationToken));
        Assert.Equal("TS_META_NO_DB", ex.Code);
    }

    [Fact]
    public async Task Missing_named_file_is_TS_DB_MISSING_without_fallback()
    {
        // tokensave.db exists, but the metadata names a different, missing file.
        new SyntheticTokenSave { BranchMetaJson = SyntheticTokenSave.BranchMeta("main", ("main", "gone.db")) }.Build(_ws.Path);
        var ex = await Assert.ThrowsAsync<ActiveDatabaseResolutionException>(() => Resolver("main").ResolveAsync(_ws.Path, ct: TestContext.Current.CancellationToken));
        Assert.Equal("TS_DB_MISSING", ex.Code);
    }

    [Fact]
    public async Task Missing_default_database_is_TS_DB_MISSING()
    {
        new SyntheticTokenSave { CreateDbFile = false }.Build(_ws.Path);
        var ex = await Assert.ThrowsAsync<ActiveDatabaseResolutionException>(() => Resolver("main").ResolveAsync(_ws.Path, ct: TestContext.Current.CancellationToken));
        Assert.Equal("TS_DB_MISSING", ex.Code);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    public async Task Db_file_escaping_the_tokensave_dir_is_rejected(string dbFile)
    {
        new SyntheticTokenSave { BranchMetaJson = SyntheticTokenSave.BranchMeta("main", ("main", dbFile)), CreateDbFile = false }.Build(_ws.Path);
        var ex = await Assert.ThrowsAsync<ActiveDatabaseResolutionException>(() => Resolver("main").ResolveAsync(_ws.Path, ct: TestContext.Current.CancellationToken));
        Assert.Equal("TS_META_NO_DB", ex.Code);
    }
}

public class ActiveDatabaseResolverEdgeTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private async Task<ActiveDatabaseResolutionException> Fail(string metaJson)
    {
        new SyntheticTokenSave { BranchMetaJson = metaJson }.Build(_ws.Path);
        var resolver = new ActiveDatabaseResolver((_, _) => Task.FromResult<string?>("main"));
        return await Assert.ThrowsAsync<ActiveDatabaseResolutionException>(() => resolver.ResolveAsync(_ws.Path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Metadata_that_is_not_a_json_object_is_TS_META_MALFORMED() =>
        Assert.Equal("TS_META_MALFORMED", (await Fail("[]")).Code);

    [Fact]
    public async Task Db_file_pointing_outside_tokensave_is_refused() =>
        Assert.Equal("TS_META_NO_DB", (await Fail(SyntheticTokenSave.BranchMeta("main", ("main", "../outside.db")))).Code);

    [Fact]
    public async Task Entry_without_db_file_is_TS_META_NO_DB() =>
        Assert.Equal("TS_META_NO_DB", (await Fail("{\"default_branch\":\"main\",\"branches\":{\"main\":{}}}")).Code);

    [Fact]
    public async Task Db_file_that_does_not_exist_is_TS_DB_MISSING() =>
        Assert.Equal("TS_DB_MISSING", (await Fail(SyntheticTokenSave.BranchMeta("main", ("main", "ghost.db")))).Code);

    [Fact]
    public async Task Default_resolver_asks_git_for_the_branch_and_survives_a_non_repository()
    {
        new SyntheticTokenSave().Build(_ws.Path);

        var r = await new ActiveDatabaseResolver().ResolveAsync(_ws.Path, TestContext.Current.CancellationToken);

        Assert.Equal("default", r.Resolution);
    }
}

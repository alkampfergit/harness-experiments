using Polifemo.Contracts.Errors;
using Polifemo.Platform.Fixtures;
using Polifemo.Platform.TokenSave;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Fixtures;

public class WorktreeIsolationTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Dirty_worktree_keeps_its_edits_and_the_original_stays_clean()
    {
        var built = await SyntheticFixture.SharedAsync();
        var main = await SyntheticFixture.CloneAsync(_ws, built, "main");

        var dirty = await WorktreeFactory.AddAsync(main, "dirty", SyntheticFixture.MutationDir("edit-method"), ct: TestContext.Current.CancellationToken);

        Assert.True(dirty.Dirty);
        Assert.Equal(["src/Domain/Money.cs"], dirty.ChangedPaths);
        Assert.Equal("M src/Domain/Money.cs", await Git.RunAsync(dirty.Path, ["status", "--porcelain"], ct: TestContext.Current.CancellationToken));  // RunAsync trims the leading space
        Assert.Equal("", await Git.RunAsync(main, ["status", "--porcelain"], ct: TestContext.Current.CancellationToken));
        Assert.Contains("decimal.Round", File.ReadAllText(Path.Combine(dirty.Path, "src/Domain/Money.cs")));
        Assert.DoesNotContain("decimal.Round", File.ReadAllText(Path.Combine(main, "src/Domain/Money.cs")));
    }

    [Fact]
    public async Task The_worktree_is_registered_in_the_clone_and_never_in_the_source()
    {
        var built = await SyntheticFixture.SharedAsync();
        var main = await SyntheticFixture.CloneAsync(_ws, built, "main");
        var sourceStatus = await Git.RunAsync(built.RepositoryPath, ["--no-optional-locks", "status", "--porcelain=v1", "--ignored"], ct: TestContext.Current.CancellationToken);

        var added = await WorktreeFactory.AddAsync(main, "second", ct: TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(Path.Combine(main, ".git", "worktrees")));
        Assert.False(Directory.Exists(Path.Combine(built.RepositoryPath, ".git", "worktrees")));
        Assert.Equal(sourceStatus, await Git.RunAsync(built.RepositoryPath, ["--no-optional-locks", "status", "--porcelain=v1", "--ignored"], ct: TestContext.Current.CancellationToken));
        Assert.False(added.Dirty);
        Assert.StartsWith(_ws.Combine("work"), added.Path);
    }

    [Fact]
    public async Task Reverting_the_dirty_edits_restores_the_original_tree()
    {
        var built = await SyntheticFixture.SharedAsync();
        var main = await SyntheticFixture.CloneAsync(_ws, built, "main");
        var dirty = await WorktreeFactory.AddAsync(main, "dirty", SyntheticFixture.MutationDir("edit-method"), ct: TestContext.Current.CancellationToken);

        await Git.RunAsync(dirty.Path, ["checkout", "--", "."], ct: TestContext.Current.CancellationToken);

        Assert.Equal("", await Git.RunAsync(dirty.Path, ["status", "--porcelain"], ct: TestContext.Current.CancellationToken));
        Assert.Equal(File.ReadAllBytes(Path.Combine(main, "src/Domain/Money.cs")), File.ReadAllBytes(Path.Combine(dirty.Path, "src/Domain/Money.cs")));
    }

    [Fact]
    public async Task Existing_worktree_name_is_refused_and_bad_names_too()
    {
        var built = await SyntheticFixture.SharedAsync();
        var main = await SyntheticFixture.CloneAsync(_ws, built, "main");
        await WorktreeFactory.AddAsync(main, "w", ct: TestContext.Current.CancellationToken);

        Assert.Equal("PREP_TARGET_EXISTS", (await Assert.ThrowsAsync<FixturePreparationException>(() => WorktreeFactory.AddAsync(main, "w", ct: TestContext.Current.CancellationToken))).Code);
        Assert.Equal("PREP_BAD_NAME", (await Assert.ThrowsAsync<FixturePreparationException>(() => WorktreeFactory.AddAsync(main, "../w", ct: TestContext.Current.CancellationToken))).Code);
    }

    [Fact]
    [Trait("Category", "TokenSave")]
    public async Task Each_worktree_has_its_own_tokensave_index_with_its_own_content()
    {
        RequireTokenSave.Ensure();
        var built = await SyntheticFixture.SharedAsync();
        var main = await SyntheticFixture.CloneAsync(_ws, built, "main", index: true);
        var dirty = await WorktreeFactory.AddAsync(main, "dirty", SyntheticFixture.MutationDir("edit-method"), ct: TestContext.Current.CancellationToken);
        var init = await Polifemo.Platform.Processes.ProcessRunner.RunAsync("tokensave", ["init", dirty.Path], dirty.Path, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);

        var mainGraph = await TokenSaveGraph.LoadAsync(main, ct: TestContext.Current.CancellationToken);
        var dirtyGraph = await TokenSaveGraph.LoadAsync(dirty.Path, ct: TestContext.Current.CancellationToken);

        Assert.NotEqual(mainGraph.DatabasePath, dirtyGraph.DatabasePath);
        string Hash(TokenSaveGraph g, string path) => g.Files.Single(f => f.Path == path).ContentHash;
        Assert.NotEqual(Hash(mainGraph, "src/Domain/Money.cs"), Hash(dirtyGraph, "src/Domain/Money.cs"));
        Assert.Equal(Hash(mainGraph, "src/Domain/Order.cs"), Hash(dirtyGraph, "src/Domain/Order.cs"));
    }
}

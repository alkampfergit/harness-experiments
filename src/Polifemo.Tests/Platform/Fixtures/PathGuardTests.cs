using Polifemo.Contracts.Errors;
using Polifemo.Platform.Fixtures;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Fixtures;

public sealed class PathGuardTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Build_refuses_an_output_that_overlaps_the_fixture()
    {
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() =>
            FixtureBuilder.BuildAsync(_ws.Path, _ws.Combine("out"), TestContext.Current.CancellationToken));
        Assert.Equal("FIX_BAD_OUT", ex.Code);
    }

    [Fact]
    public async Task Build_refuses_the_index_root_itself_as_output()
    {
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() =>
            FixtureBuilder.BuildAsync(_ws.Path, Path.Combine(Path.GetTempPath(), "polifemo-guard", ".index"),
                TestContext.Current.CancellationToken));
        Assert.Equal("FIX_BAD_OUT", ex.Code);
    }

    [Fact]
    public async Task Build_refuses_an_output_that_reaches_the_fixture_through_a_symlink()
    {
        var index = Path.Combine(Path.GetTempPath(), "polifemo-guard-link", ".index");
        Directory.CreateDirectory(index);
        var alias = Path.Combine(index, "alias");
        if (Directory.Exists(alias)) Directory.Delete(alias);
        Directory.CreateSymbolicLink(alias, _ws.Path);
        try
        {
            var ex = await Assert.ThrowsAsync<FixturePreparationException>(() =>
                FixtureBuilder.BuildAsync(_ws.Path, Path.Combine(alias, "corpus"), TestContext.Current.CancellationToken));
            Assert.Equal("FIX_BAD_OUT", ex.Code);
            Assert.True(Directory.Exists(_ws.Path));
        }
        finally
        {
            Directory.Delete(alias);
        }
    }

    [Fact]
    public async Task Build_refuses_an_output_that_resolves_outside_the_index_folder()
    {
        var index = Path.Combine(Path.GetTempPath(), "polifemo-guard-out", ".index");
        var victim = Path.Combine(Path.GetTempPath(), "polifemo-guard-out", "victim");
        Directory.CreateDirectory(index);
        Directory.CreateDirectory(victim);
        var marker = Path.Combine(victim, "keep.txt");
        File.WriteAllText(marker, "keep");
        var alias = Path.Combine(index, "alias");
        if (Directory.Exists(alias)) Directory.Delete(alias);
        Directory.CreateSymbolicLink(alias, Path.GetDirectoryName(victim)!);
        try
        {
            var ex = await Assert.ThrowsAsync<FixturePreparationException>(() =>
                FixtureBuilder.BuildAsync(_ws.Path, Path.Combine(alias, "victim"), TestContext.Current.CancellationToken));
            Assert.Equal("FIX_BAD_OUT", ex.Code);
            Assert.True(File.Exists(marker));
        }
        finally
        {
            Directory.Delete(alias);
        }
    }

    [Fact]
    public async Task Mutate_refuses_a_worktree_outside_an_index_folder()
    {
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() =>
            MutationApplier.ApplyAsync(Path.Combine(Path.GetTempPath(), "polifemo-not-index"), SyntheticFixture.MutationDir("edit-method"),
                ct: TestContext.Current.CancellationToken));
        Assert.Equal("MUT_BAD_WORKTREE", ex.Code);
    }

    [Fact]
    public async Task Worktree_refuses_a_repository_that_is_not_a_prepared_clone()
    {
        var built = await SyntheticFixture.SharedAsync();
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() =>
            WorktreeFactory.AddAsync(built.RepositoryPath, "w", ct: TestContext.Current.CancellationToken));
        Assert.Equal("PREP_NOT_PREPARED", ex.Code);
    }

    [Fact]
    public async Task Prepare_refuses_a_target_that_is_the_source()
    {
        var built = await SyntheticFixture.SharedAsync();
        var root = Path.GetDirectoryName(built.RepositoryPath)!;
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() =>
            new SamplePreparer(root).PrepareAsync(built.RepositoryPath, built.Commit, Path.GetFileName(built.RepositoryPath), replace: true,
                ct: TestContext.Current.CancellationToken));
        Assert.Equal("PREP_BAD_NAME", ex.Code);
        Assert.True(Directory.Exists(built.RepositoryPath));
    }
}

using Polifemo.Platform.Fixtures;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Fixtures;

public class GitInfoTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Repository_root_reports_branch_and_head_commit()
    {
        var repo = await GitRepo.CreateAsync(_ws.Combine("r"));
        repo.Write("a.txt", "a");
        var commit = await repo.CommitAllAsync();

        Assert.Equal("main", await GitInfo.BranchAsync(repo.Path, TestContext.Current.CancellationToken));
        Assert.Equal(commit, await GitInfo.HeadCommitAsync(repo.Path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Detached_head_has_no_branch_but_still_a_commit()
    {
        var repo = await GitRepo.CreateAsync(_ws.Combine("r"));
        repo.Write("a.txt", "a");
        var commit = await repo.CommitAllAsync();
        await repo.Run("checkout", "-q", "--detach");

        Assert.Null(await GitInfo.BranchAsync(repo.Path, TestContext.Current.CancellationToken));
        Assert.Equal(commit, await GitInfo.HeadCommitAsync(repo.Path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Repository_without_commits_has_no_head_commit()
    {
        var repo = await GitRepo.CreateAsync(_ws.Combine("r"));

        Assert.Null(await GitInfo.HeadCommitAsync(repo.Path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Subfolder_of_a_repository_and_non_repositories_report_nothing()
    {
        var repo = await GitRepo.CreateAsync(_ws.Combine("r"));
        repo.Write("sub/a.txt", "a");
        await repo.CommitAllAsync();
        var plain = _ws.Combine("plain");
        Directory.CreateDirectory(plain);

        Assert.Null(await GitInfo.BranchAsync(repo.File("sub"), TestContext.Current.CancellationToken));
        Assert.Null(await GitInfo.HeadCommitAsync(repo.File("sub"), TestContext.Current.CancellationToken));
        Assert.Null(await GitInfo.BranchAsync(Path.Combine(_ws.Path, "missing"), TestContext.Current.CancellationToken));
    }
}

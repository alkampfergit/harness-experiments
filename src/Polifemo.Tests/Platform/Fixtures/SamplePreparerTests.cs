using Polifemo.Contracts.Errors;
using Polifemo.Platform.Fixtures;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Fixtures;

public class SamplePreparerTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private SamplePreparer Preparer => new(_ws.Combine("work"));

    private async Task<(GitRepo Repo, string Commit)> SampleAsync()
    {
        var repo = await GitRepo.CreateAsync(_ws.Combine("sample"));
        repo.Write(".gitignore", "ignored.txt\n");
        repo.Write("a.cs", "class A {}\n");
        repo.Write("crlf.cs", "class C {}\r\n"u8.ToArray());
        var commit = await repo.CommitAllAsync();
        repo.Write("untracked.txt", "u");
        repo.Write("ignored.txt", "i");
        return (repo, commit);
    }

    [Fact]
    public async Task Writes_only_under_work_root_and_leaves_the_source_untouched()
    {
        var (repo, commit) = await SampleAsync();
        var statusBefore = await repo.StatusAsync();

        var result = await Preparer.PrepareAsync(repo.Path, commit, "one");

        Assert.Equal(Path.GetFullPath(_ws.Combine("work", "one")), result.WorkDir);
        Assert.Equal(commit, result.Commit);
        Assert.True(result.SourceUnchanged);
        Assert.Equal(statusBefore, await repo.StatusAsync());
        Assert.Equal(commit, await repo.Head());
        Assert.False(Directory.Exists(Path.Combine(repo.Path, ".tokensave")));
        Assert.Equal(commit, await Git.RunAsync(result.WorkDir, ["rev-parse", "HEAD"]));
        Assert.True(File.Exists(Path.Combine(result.WorkDir, ".polifemo-prep.json")));
        Assert.Contains(commit, File.ReadAllText(Path.Combine(result.WorkDir, ".polifemo-prep.json")));
    }

    [Fact]
    public async Task Clone_keeps_bytes_exactly_and_does_not_check_out_untracked_or_ignored_files()
    {
        var (repo, commit) = await SampleAsync();
        var result = await Preparer.PrepareAsync(repo.Path, commit, "bytes");

        Assert.Equal(File.ReadAllBytes(repo.File("crlf.cs")), File.ReadAllBytes(Path.Combine(result.WorkDir, "crlf.cs")));
        Assert.False(File.Exists(Path.Combine(result.WorkDir, "untracked.txt")));
        Assert.False(File.Exists(Path.Combine(result.WorkDir, "ignored.txt")));
    }

    [Fact]
    public async Task Clone_is_detached_and_does_not_run_hooks_from_anywhere()
    {
        var (repo, commit) = await SampleAsync();
        var result = await Preparer.PrepareAsync(repo.Path, commit, "hooks");

        var hooksPath = await Git.RunAsync(result.WorkDir, ["config", "core.hooksPath"]);
        Assert.Equal(Path.GetFullPath(_ws.Combine("work", ".empty-hooks")), hooksPath);
        Assert.Empty(Directory.GetFileSystemEntries(hooksPath));
        Assert.Equal("HEAD", await Git.RunAsync(result.WorkDir, ["rev-parse", "--abbrev-ref", "HEAD"]));
    }

    [Fact]
    public async Task Resolves_a_short_sha_or_branch_to_the_full_commit()
    {
        var (repo, commit) = await SampleAsync();
        var viaBranch = await Preparer.PrepareAsync(repo.Path, "main", "branch");
        var viaShort = await Preparer.PrepareAsync(repo.Path, commit[..9], "short");
        Assert.Equal(commit, viaBranch.Commit);
        Assert.Equal(commit, viaShort.Commit);
    }

    [Fact]
    public async Task Unknown_commit_fails_and_leaves_nothing_behind()
    {
        var (repo, _) = await SampleAsync();
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(
            () => Preparer.PrepareAsync(repo.Path, new string('a', 40), "nope"));

        Assert.Equal("PREP_UNKNOWN_COMMIT", ex.Code);
        Assert.Empty(Directory.Exists(_ws.Combine("work")) ? Directory.GetDirectories(_ws.Combine("work"), "nope*") : []);
    }

    [Theory]
    [InlineData("--upload-pack=x")]
    [InlineData("")]
    public async Task Option_like_or_empty_commit_is_rejected(string commit)
    {
        var (repo, _) = await SampleAsync();
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() => Preparer.PrepareAsync(repo.Path, commit, "x"));
        Assert.Equal("PREP_UNKNOWN_COMMIT", ex.Code);
    }

    [Fact]
    public async Task Plain_directory_and_missing_path_are_PREP_NOT_A_REPO()
    {
        Directory.CreateDirectory(_ws.Combine("plain"));
        foreach (var path in new[] { _ws.Combine("plain"), _ws.Combine("missing") })
        {
            var ex = await Assert.ThrowsAsync<FixturePreparationException>(() => Preparer.PrepareAsync(path, "HEAD", "x"));
            Assert.Equal("PREP_NOT_A_REPO", ex.Code);
        }
        Assert.False(Directory.Exists(_ws.Combine("work", "x")));
    }

    [Fact]
    public async Task A_subfolder_of_a_repository_is_not_a_sample_root()
    {
        var (repo, commit) = await SampleAsync();
        repo.Write("sub/x.txt", "x");
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(
            () => Preparer.PrepareAsync(Path.Combine(repo.Path, "sub"), commit, "sub"));
        Assert.Equal("PREP_NOT_A_REPO", ex.Code);
    }

    [Fact]
    public async Task Existing_target_needs_replace()
    {
        var (repo, commit) = await SampleAsync();
        await Preparer.PrepareAsync(repo.Path, commit, "twice");
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() => Preparer.PrepareAsync(repo.Path, commit, "twice"));
        Assert.Equal("PREP_TARGET_EXISTS", ex.Code);

        var again = await Preparer.PrepareAsync(repo.Path, commit, "twice", replace: true);
        Assert.Equal(commit, again.Commit);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(".hidden")]
    [InlineData("x.tmp")]
    [InlineData("")]
    [InlineData("..")]
    public async Task Names_that_could_leave_the_work_root_are_refused(string name)
    {
        var (repo, commit) = await SampleAsync();
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() => Preparer.PrepareAsync(repo.Path, commit, name));
        Assert.Equal("PREP_BAD_NAME", ex.Code);
        Assert.False(Directory.Exists(_ws.Combine("escape")));
    }

    [Fact]
    [Trait("Category", "TokenSave")]
    public async Task Indexing_creates_tokensave_only_in_the_clone()
    {
        RequireTokenSave.Ensure();
        var (repo, commit) = await SampleAsync();
        repo.Write("B.cs", "namespace N { public class B { public void M() {} } }\n");
        commit = await repo.CommitAllAsync("b");
        var statusBefore = await repo.StatusAsync();

        var result = await Preparer.PrepareAsync(repo.Path, commit, "indexed", index: true);

        Assert.True(result.Indexed);
        Assert.True(File.Exists(Path.Combine(result.WorkDir, ".tokensave", "tokensave.db")));
        Assert.False(Directory.Exists(Path.Combine(repo.Path, ".tokensave")));
        Assert.Equal(statusBefore, await repo.StatusAsync());
    }
}

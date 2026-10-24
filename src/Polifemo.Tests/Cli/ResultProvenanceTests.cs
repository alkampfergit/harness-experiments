using System.Text.Json;
using Polifemo.Cli;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Cli;

public class ResultProvenanceTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_git_worktree_gets_its_head_commit_as_snapshot_and_source_commit()
    {
        var repo = await GitRepo.CreateAsync(_ws.Combine("repo"));
        repo.Write("a.txt", "a");
        var head = await repo.CommitAllAsync();

        var p = await ResultProvenance.ForWorktreeAsync(repo.Path, "7.16.0");

        Assert.Equal(head, p.SourceCommit);
        Assert.Equal(head, p.SnapshotId);
        Assert.Equal("7.16.0", p.ExtractorVersion);
        Assert.Equal("repo", p.Scope.WorktreeId);
        Assert.Equal("local", p.Scope.AuthorizationBoundary);
    }

    [Fact]
    public async Task A_directory_that_is_not_a_repository_is_reported_as_unknown_not_invented()
    {
        Directory.CreateDirectory(_ws.Combine("plain"));
        var p = await ResultProvenance.ForWorktreeAsync(_ws.Combine("plain"), "7.16.0");
        Assert.Equal("unknown", p.SourceCommit);
    }

    [Fact]
    public async Task Platform_check_output_carries_provenance()
    {
        new SyntheticTokenSave().Build(_ws.Path);
        var stdout = new StringWriter();

        await CliApp.RunAsync(["platform", "check", "--worktree", _ws.Path], stdout, new StringWriter(),
            binaryVersion: _ => Task.FromResult(PlatformPins.TokenSave));

        var provenance = JsonDocument.Parse(stdout.ToString()).RootElement.GetProperty("provenance");
        Assert.Equal(PlatformPins.TokenSave, provenance.GetProperty("extractorVersion").GetString());
        Assert.True(provenance.TryGetProperty("scope", out _));
        Assert.True(provenance.TryGetProperty("sourceCommit", out _));
    }
}

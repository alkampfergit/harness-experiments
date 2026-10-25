using System.CommandLine;
using System.Text.Json;
using Polifemo.Cli;
using Polifemo.Platform.Fixtures;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Cli;

public class FixtureCommandTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private async Task<(int Exit, JsonElement Out, string Err)> Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var root = new RootCommand();
        root.Subcommands.Add(FixtureCommands.Create(stdout, stderr, () => _ws.Combine("work")));
        var parse = root.Parse(args);
        Assert.Empty(parse.Errors);
        var exit = await parse.InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr }, TestContext.Current.CancellationToken);
        var text = stdout.ToString();
        return (exit, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), stderr.ToString());
    }

    private async Task<(string Repo, string Commit)> SourceAsync()
    {
        var built = await SyntheticFixture.SharedAsync();
        return (built.RepositoryPath, built.Commit);
    }

    [Fact]
    public async Task Build_prints_commit_and_tree_matching_the_library()
    {
        var (exit, output, _) = await Run("fixture", "build", "--fixture", SyntheticFixture.Directory, "--out", _ws.Combine("built"));

        Assert.Equal(0, exit);
        var shared = await SyntheticFixture.SharedAsync();
        Assert.Equal(shared.Commit, output.GetProperty("commit").GetString());
        Assert.Equal(shared.TreeSha, output.GetProperty("treeSha").GetString());
    }

    [Fact]
    public async Task Build_outside_the_index_folder_exits_2()
    {
        var outside = Path.Combine(Path.GetTempPath(), "polifemo-not-index-" + Guid.NewGuid().ToString("N"));

        var (exit, _, err) = await Run("fixture", "build", "--fixture", SyntheticFixture.Directory, "--out", outside);

        Assert.Equal(2, exit);
        Assert.Contains("FIX_BAD_OUT", err);
        Assert.False(Directory.Exists(outside));
    }

    [Fact]
    public async Task Prepare_then_worktree_then_mutate_chain_through_the_cli()
    {
        var (repo, commit) = await SourceAsync();

        var (pExit, prepared, _) = await Run("fixture", "prepare", "--sample", repo, "--commit", commit, "--name", "cli");
        Assert.Equal(0, pExit);
        var work = prepared.GetProperty("workDir").GetString()!;
        Assert.Equal(commit, prepared.GetProperty("commit").GetString());
        Assert.True(prepared.GetProperty("sourceUnchanged").GetBoolean());
        Assert.False(prepared.GetProperty("indexed").GetBoolean());
        Assert.True(prepared.TryGetProperty("provenance", out _));

        var (wExit, wt, _) = await Run("fixture", "worktree", "--work", work, "--name", "dirty", "--dirty-from", "add-file");
        Assert.Equal(0, wExit);
        Assert.True(wt.GetProperty("dirty").GetBoolean());
        Assert.NotEmpty(wt.GetProperty("changedPaths").EnumerateArray());
        Assert.True(Directory.Exists(wt.GetProperty("worktree").GetString()));

        var (mExit, mut, _) = await Run("fixture", "mutate", "--worktree", work, "--mutation", "edit-method");
        Assert.Equal(0, mExit);
        Assert.Equal(commit, mut.GetProperty("base").GetString());
        Assert.False(string.IsNullOrEmpty(mut.GetProperty("commit").GetString()));
        Assert.NotEmpty(mut.GetProperty("changedPaths").EnumerateArray());
    }

    [Fact]
    public async Task Mutate_with_no_commit_leaves_the_changes_uncommitted()
    {
        var (repo, commit) = await SourceAsync();
        var (_, prepared, _) = await Run("fixture", "prepare", "--sample", repo, "--commit", commit, "--name", "nocommit");
        var work = prepared.GetProperty("workDir").GetString()!;

        var (exit, mut, _) = await Run("fixture", "mutate", "--worktree", work, "--mutation", "edit-method", "--no-commit");

        Assert.Equal(0, exit);
        Assert.True(!mut.TryGetProperty("commit", out var c) || c.ValueKind == JsonValueKind.Null);
        Assert.Equal(commit, await Git.RunAsync(work, ["rev-parse", "HEAD"], ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Preparing_an_unknown_commit_exits_1_and_an_invalid_name_exits_2()
    {
        var (repo, commit) = await SourceAsync();

        var (unknown, _, _) = await Run("fixture", "prepare", "--sample", repo, "--commit", new string('0', 40), "--name", "x");
        var (badName, _, _) = await Run("fixture", "prepare", "--sample", repo, "--commit", commit, "--name", "../escape");

        Assert.Equal(1, unknown);
        Assert.Equal(2, badName);
    }

    [Fact]
    public async Task Mutation_given_as_an_existing_folder_is_used_as_is()
    {
        var (repo, commit) = await SourceAsync();
        var (_, prepared, _) = await Run("fixture", "prepare", "--sample", repo, "--commit", commit, "--name", "folder");
        var work = prepared.GetProperty("workDir").GetString()!;

        var (exit, _, _) = await Run("fixture", "mutate", "--worktree", work, "--mutation", SyntheticFixture.MutationDir("edit-method"));

        Assert.Equal(0, exit);
    }
}

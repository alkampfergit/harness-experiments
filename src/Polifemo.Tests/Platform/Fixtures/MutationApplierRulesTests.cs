using Polifemo.Contracts.Errors;
using Polifemo.Platform.Fixtures;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Fixtures;

/// <summary>Mutation rules on a hand-made repository: what an op does, and what it must refuse.</summary>
public sealed class MutationApplierRulesTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private async Task<GitRepo> RepoAsync()
    {
        var repo = await GitRepo.CreateAsync(_ws.Combine("wt"));
        repo.Write("a.txt", "one\ntwo\nthree\n");
        repo.Write("b.txt", "bee\n");
        await repo.CommitAllAsync();
        return repo;
    }

    private string Mutation(string opsJson, string @base = "initial", params (string File, string Content)[] payloads)
    {
        var dir = _ws.Combine("mut" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mutation.json"), $"{{\"name\":\"t\",\"base\":\"{@base}\",\"ops\":[{opsJson}]}}");
        foreach (var (f, c) in payloads) File.WriteAllText(Path.Combine(dir, f), c);
        return dir;
    }

    private static Task<MutationResult> Apply(GitRepo repo, string dir, bool commit = true) =>
        MutationApplier.ApplyAsync(repo.Path, dir, commit, TestContext.Current.CancellationToken);

    private static async Task<string> CodeOf(Task<MutationResult> task) =>
        (await Assert.ThrowsAsync<FixturePreparationException>(() => task)).Code;

    [Fact]
    public async Task Initial_base_means_the_root_commit_and_ops_apply_in_order()
    {
        var repo = await RepoAsync();
        var root = await repo.Head();
        var dir = Mutation(
            """
            {"op":"write","path":"new/c.txt","payloadFile":"p"},
            {"op":"replaceBytes","path":"b.txt","payloadFile":"p"},
            {"op":"insertLines","path":"a.txt","payloadFile":"p","atLine":2},
            {"op":"rename","from":"a.txt","to":"moved/a.txt"}
            """, payloads: ("p", "X\n"));

        var result = await Apply(repo, dir);

        Assert.Equal(root, result.Base);
        Assert.Equal(["a.txt", "b.txt", "moved/a.txt", "new/c.txt"], result.ChangedPaths);
        Assert.Equal("X\n", File.ReadAllText(repo.File("b.txt")));
        Assert.False(File.Exists(repo.File("a.txt")));
        Assert.Equal(result.Commit, await repo.Head());
    }

    [Fact]
    public async Task Delete_removes_the_file_and_lists_it_as_changed()
    {
        var repo = await RepoAsync();

        var result = await Apply(repo, Mutation("""{"op":"delete","path":"b.txt"}"""));

        Assert.Equal(["b.txt"], result.ChangedPaths);
        Assert.False(File.Exists(repo.File("b.txt")));
        Assert.Equal("", await repo.Run("status", "--porcelain"));
    }

    [Fact]
    public async Task Insert_lines_puts_the_payload_before_the_given_line()
    {
        var repo = await RepoAsync();
        await Apply(repo, Mutation("""{"op":"insertLines","path":"a.txt","payloadFile":"p","atLine":2}""", payloads: ("p", "X\n")));

        Assert.Equal("one\nX\ntwo\nthree\n", File.ReadAllText(repo.File("a.txt")));
    }

    [Fact]
    public async Task Base_that_is_not_head_is_MUT_BASE_MISMATCH_and_nothing_is_touched()
    {
        var repo = await RepoAsync();
        var dir = Mutation("""{"op":"delete","path":"b.txt"}""", @base: new string('a', 40));

        Assert.Equal("MUT_BASE_MISMATCH", await CodeOf(Apply(repo, dir)));
        Assert.True(File.Exists(repo.File("b.txt")));
    }

    [Theory]
    [InlineData("""{"op":"explode","path":"a.txt"}""", "MUT_INVALID")]
    [InlineData("""{"op":"write","path":"x.txt"}""", "MUT_INVALID")]
    [InlineData("""{"op":"write","path":"x.txt","payloadFile":"../p"}""", "MUT_BAD_PATH")]
    [InlineData("""{"op":"delete","path":"missing.txt"}""", "MUT_INVALID")]
    [InlineData("""{"op":"replaceBytes","path":"missing.txt","payloadFile":"p"}""", "MUT_INVALID")]
    [InlineData("""{"op":"rename","from":"missing.txt","to":"x.txt"}""", "MUT_INVALID")]
    [InlineData("""{"op":"insertLines","path":"a.txt","payloadFile":"p"}""", "MUT_INVALID")]
    [InlineData("""{"op":"insertLines","path":"a.txt","payloadFile":"p","atLine":99}""", "MUT_INVALID")]
    [InlineData("""{"op":"write","path":"../escape.txt","payloadFile":"p"}""", "MUT_BAD_PATH")]
    [InlineData("""{"op":"write","path":"/abs.txt","payloadFile":"p"}""", "MUT_BAD_PATH")]
    [InlineData("""{"op":"write","path":".git/config","payloadFile":"p"}""", "MUT_BAD_PATH")]
    [InlineData("""{"op":"delete","path":""}""", "MUT_BAD_PATH")]
    public async Task Invalid_or_escaping_ops_are_refused_with_a_named_code(string op, string code)
    {
        var repo = await RepoAsync();
        var dir = Mutation(op, payloads: ("p", "X\n"));

        Assert.Equal(code, await CodeOf(Apply(repo, dir)));
    }

    [Fact]
    public async Task Missing_or_malformed_mutation_file_is_MUT_INVALID()
    {
        var repo = await RepoAsync();
        var empty = _ws.Combine("empty");
        Directory.CreateDirectory(empty);
        var broken = _ws.Combine("broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "mutation.json"), "{ not json");
        var noOps = _ws.Combine("noops");
        Directory.CreateDirectory(noOps);
        File.WriteAllText(Path.Combine(noOps, "mutation.json"), "{\"name\":\"x\",\"base\":\"initial\"}");

        Assert.Equal("MUT_INVALID", await CodeOf(Apply(repo, empty)));
        Assert.Equal("MUT_INVALID", await CodeOf(Apply(repo, broken)));
        Assert.Equal("MUT_INVALID", await CodeOf(Apply(repo, noOps)));
    }

    [Fact]
    public async Task Worktree_outside_an_index_folder_is_MUT_BAD_WORKTREE()
    {
        var outside = Path.Combine(Path.GetTempPath(), "polifemo-mut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            var dir = Mutation("""{"op":"delete","path":"b.txt"}""");
            Assert.Equal("MUT_BAD_WORKTREE", await CodeOf(MutationApplier.ApplyAsync(outside, dir, ct: TestContext.Current.CancellationToken)));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }
}

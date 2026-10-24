using System.Text.Json;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.Fixtures;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Fixtures;

/// <summary>
/// One prepared clone shared by the tests of this class. Tests of a class run one after the other, and
/// every test starts with <see cref="ResetAsync"/>, so the clone is as good as a fresh one at a fraction of the cost.
/// </summary>
public sealed class MutationClone : IAsyncLifetime
{
    private readonly TestWorkspace _ws = new();
    public string Worktree { get; private set; } = "";
    public BuiltFixture Source { get; private set; } = null!;
    public TestWorkspace Workspace => _ws;

    public async ValueTask InitializeAsync()
    {
        Source = await SyntheticFixture.SharedAsync();
        Worktree = await SyntheticFixture.CloneAsync(_ws, Source, "mutations");
    }

    /// <summary>Back to the base commit with no leftovers. Ignored files (.tokensave, prep file) are kept.</summary>
    public async Task ResetAsync()
    {
        await Git.RunAsync(Worktree, ["reset", "-q", "--hard", Source.Commit]);
        await Git.RunAsync(Worktree, ["clean", "-q", "-fd"]);
    }

    public ValueTask DisposeAsync()
    {
        _ws.Dispose();
        return ValueTask.CompletedTask;
    }
}

public class MutationApplierTests(MutationClone clone) : IClassFixture<MutationClone>
{
    public static TheoryData<string> Mutations => SyntheticFixture.MutationData();

    private string Work => clone.Worktree;

    [Fact]
    public void All_seven_required_mutations_and_the_edge_case_exist()
    {
        Assert.Equal(
            ["add-file", "change-doc-association", "delete-file", "docs-only-edit", "edit-method", "line-shift", "rename-and-edit", "rename-file"],
            SyntheticFixture.MutationNames());
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task Same_base_gives_the_same_commit_and_tree_and_the_uncommitted_tree_agrees(string mutation)
    {
        var dir = SyntheticFixture.MutationDir(mutation);

        await clone.ResetAsync();
        var first = await MutationApplier.ApplyAsync(Work, dir);
        Assert.Equal("", await Git.RunAsync(Work, ["status", "--porcelain"]));

        await clone.ResetAsync();
        var second = await MutationApplier.ApplyAsync(Work, dir);

        Assert.Equal(clone.Source.Commit, first.Base);
        Assert.NotEqual(clone.Source.TreeSha, first.TreeSha);
        Assert.NotEmpty(first.ChangedPaths);
        Assert.Equal(first.Commit, second.Commit);
        Assert.Equal(first.TreeSha, second.TreeSha);
        Assert.Equal(first.ChangedPaths, second.ChangedPaths);

        await clone.ResetAsync();
        var dirty = await MutationApplier.ApplyAsync(Work, dir, commit: false);

        Assert.Null(dirty.Commit);
        Assert.Equal(first.TreeSha, dirty.TreeSha);
        Assert.Equal(clone.Source.Commit, await Git.RunAsync(Work, ["rev-parse", "HEAD"]));
        Assert.NotEqual("", await Git.RunAsync(Work, ["status", "--porcelain"]));
    }

    [Fact]
    public async Task Two_independent_clones_give_the_same_result()
    {
        // The other tests reuse one clone. This one proves the property across fresh preparations (SC-003).
        using var ws = new TestWorkspace();
        var a = await SyntheticFixture.CloneAsync(ws, clone.Source, "a");
        var b = await SyntheticFixture.CloneAsync(ws, clone.Source, "b");
        var dir = SyntheticFixture.MutationDir("rename-and-edit");

        var first = await MutationApplier.ApplyAsync(a, dir);
        var second = await MutationApplier.ApplyAsync(b, dir);

        Assert.Equal(first.Commit, second.Commit);
        Assert.Equal(first.TreeSha, second.TreeSha);
    }

    [Fact]
    public async Task Rename_keeps_the_file_bytes_so_the_blob_is_the_same()
    {
        await clone.ResetAsync();
        await MutationApplier.ApplyAsync(Work, SyntheticFixture.MutationDir("rename-file"));

        Assert.Equal(
            await Git.RunAsync(Work, ["rev-parse", $"{clone.Source.Commit}:src/Domain/OrderLine.cs"]),
            await Git.RunAsync(Work, ["rev-parse", "HEAD:src/Domain/Lines/OrderLine.cs"]));
        Assert.False(File.Exists(Path.Combine(Work, "src/Domain/OrderLine.cs")));
    }

    [Fact]
    public async Task Line_shift_adds_exactly_the_payload_lines_and_nothing_else()
    {
        await clone.ResetAsync();
        var before = File.ReadAllLines(Path.Combine(Work, "src/Domain/Money.cs"));
        await MutationApplier.ApplyAsync(Work, SyntheticFixture.MutationDir("line-shift"));
        var after = File.ReadAllLines(Path.Combine(Work, "src/Domain/Money.cs"));

        Assert.Equal(before.Length + 2, after.Length);
        Assert.Equal(before, after.Skip(2));
    }

    [Fact]
    public async Task Rename_and_edit_changes_both_the_path_and_the_content()
    {
        await clone.ResetAsync();
        var result = await MutationApplier.ApplyAsync(Work, SyntheticFixture.MutationDir("rename-and-edit"));

        Assert.Equal(["src/Services/Formatting/OrderFormatter.cs", "src/Services/OrderFormatter.cs"], result.ChangedPaths);
        Assert.NotEqual(
            await Git.RunAsync(Work, ["rev-parse", $"{clone.Source.Commit}:src/Services/OrderFormatter.cs"]),
            await Git.RunAsync(Work, ["rev-parse", "HEAD:src/Services/Formatting/OrderFormatter.cs"]));
    }

    [Fact]
    public async Task Applying_on_a_different_head_is_MUT_BASE_MISMATCH()
    {
        await clone.ResetAsync();
        await MutationApplier.ApplyAsync(Work, SyntheticFixture.MutationDir("add-file"));

        var ex = await Assert.ThrowsAsync<FixturePreparationException>(
            () => MutationApplier.ApplyAsync(Work, SyntheticFixture.MutationDir("edit-method")));
        Assert.Equal("MUT_BASE_MISMATCH", ex.Code);
    }

    [Fact]
    public async Task A_declared_base_sha_is_honoured()
    {
        await clone.ResetAsync();
        var dir = WriteMutation("by-sha", clone.Source.Commit, new { op = "write", path = "x.txt", payloadFile = "x.txt" }, ("x.txt", "x"));

        var result = await MutationApplier.ApplyAsync(Work, dir);
        Assert.Equal(["x.txt"], result.ChangedPaths);

        await clone.ResetAsync();
        var wrong = WriteMutation("wrong-sha", new string('0', 40), new { op = "write", path = "y.txt", payloadFile = "y.txt" }, ("y.txt", "y"));
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() => MutationApplier.ApplyAsync(Work, wrong));
        Assert.Equal("MUT_BASE_MISMATCH", ex.Code);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/etc/hosts")]
    [InlineData(".git/config")]
    [InlineData("src/../../outside.txt")]
    [InlineData("")]
    public async Task Paths_leaving_the_worktree_or_touching_git_are_MUT_BAD_PATH(string path)
    {
        await clone.ResetAsync();
        var dir = WriteMutation("bad", "initial", new { op = "write", path, payloadFile = "p.txt" }, ("p.txt", "p"));

        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() => MutationApplier.ApplyAsync(Work, dir));

        Assert.Equal("MUT_BAD_PATH", ex.Code);
        Assert.False(File.Exists(clone.Workspace.Combine("work", "outside.txt")));
        Assert.Equal("", await Git.RunAsync(Work, ["status", "--porcelain"]));
    }

    [Fact]
    public async Task Payload_escaping_the_mutation_directory_is_MUT_BAD_PATH()
    {
        await clone.ResetAsync();
        File.WriteAllText(clone.Workspace.Combine("secret.txt"), "s");
        var dir = WriteMutation("pl", "initial", new { op = "write", path = "x.txt", payloadFile = "../secret.txt" });

        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() => MutationApplier.ApplyAsync(Work, dir));
        Assert.Equal("MUT_BAD_PATH", ex.Code);
    }

    [Fact]
    public async Task Unknown_op_or_missing_definition_is_MUT_INVALID()
    {
        await clone.ResetAsync();

        var unknown = WriteMutation("unknown", "initial", new { op = "explode", path = "x" });
        Assert.Equal("MUT_INVALID", (await Assert.ThrowsAsync<FixturePreparationException>(() => MutationApplier.ApplyAsync(Work, unknown))).Code);

        Directory.CreateDirectory(clone.Workspace.Combine("empty"));
        Assert.Equal("MUT_INVALID", (await Assert.ThrowsAsync<FixturePreparationException>(() => MutationApplier.ApplyAsync(Work, clone.Workspace.Combine("empty")))).Code);

        var missing = WriteMutation("missing", "initial", new { op = "delete", path = "no/such/file.cs" });
        Assert.Equal("MUT_INVALID", (await Assert.ThrowsAsync<FixturePreparationException>(() => MutationApplier.ApplyAsync(Work, missing))).Code);
    }

    private string WriteMutation(string name, string @base, object op, params (string File, string Text)[] payloads)
    {
        var dir = clone.Workspace.Combine("mutations", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mutation.json"), JsonSerializer.Serialize(new { name, @base, ops = new[] { op } }));
        foreach (var (file, text) in payloads) File.WriteAllText(Path.Combine(dir, file), text);
        return dir;
    }
}

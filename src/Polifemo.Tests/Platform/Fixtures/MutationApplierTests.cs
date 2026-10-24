using System.Text.Json;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.Fixtures;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Fixtures;

public class MutationApplierTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    public static TheoryData<string> Mutations => SyntheticFixture.MutationData();

    [Fact]
    public void All_seven_required_mutations_and_the_edge_case_exist()
    {
        Assert.Equal(
            ["add-file", "change-doc-association", "delete-file", "docs-only-edit", "edit-method", "line-shift", "rename-and-edit", "rename-file"],
            SyntheticFixture.MutationNames());
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task Applying_twice_from_the_same_base_gives_identical_commit_and_tree(string mutation)
    {
        var built = await SyntheticFixture.BuildAsync(_ws);
        var a = await SyntheticFixture.CloneAsync(_ws, built, "a");
        var b = await SyntheticFixture.CloneAsync(_ws, built, "b");

        var first = await MutationApplier.ApplyAsync(a, SyntheticFixture.MutationDir(mutation));
        var second = await MutationApplier.ApplyAsync(b, SyntheticFixture.MutationDir(mutation));

        Assert.Equal(built.Commit, first.Base);
        Assert.NotEqual(built.TreeSha, first.TreeSha);
        Assert.Equal(first.Commit, second.Commit);
        Assert.Equal(first.TreeSha, second.TreeSha);
        Assert.Equal(first.ChangedPaths, second.ChangedPaths);
        Assert.NotEmpty(first.ChangedPaths);
        Assert.Equal("", await Git.RunAsync(a, ["status", "--porcelain"]));
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task Uncommitted_application_has_the_same_tree_as_the_committed_one(string mutation)
    {
        var built = await SyntheticFixture.BuildAsync(_ws);
        var committed = await SyntheticFixture.CloneAsync(_ws, built, "committed");
        var dirty = await SyntheticFixture.CloneAsync(_ws, built, "dirty");

        var c = await MutationApplier.ApplyAsync(committed, SyntheticFixture.MutationDir(mutation));
        var d = await MutationApplier.ApplyAsync(dirty, SyntheticFixture.MutationDir(mutation), commit: false);

        Assert.Null(d.Commit);
        Assert.Equal(c.TreeSha, d.TreeSha);
        Assert.Equal(built.Commit, await Git.RunAsync(dirty, ["rev-parse", "HEAD"]));
        Assert.NotEqual("", await Git.RunAsync(dirty, ["status", "--porcelain"]));
    }

    [Fact]
    public async Task Rename_keeps_the_file_bytes_so_the_blob_is_the_same()
    {
        var built = await SyntheticFixture.BuildAsync(_ws);
        var work = await SyntheticFixture.CloneAsync(_ws, built, "r");
        await MutationApplier.ApplyAsync(work, SyntheticFixture.MutationDir("rename-file"));

        Assert.Equal(
            await Git.RunAsync(work, ["rev-parse", $"{built.Commit}:src/Domain/OrderLine.cs"]),
            await Git.RunAsync(work, ["rev-parse", "HEAD:src/Domain/Lines/OrderLine.cs"]));
        Assert.False(File.Exists(Path.Combine(work, "src/Domain/OrderLine.cs")));
    }

    [Fact]
    public async Task Line_shift_adds_exactly_the_payload_lines_and_nothing_else()
    {
        var built = await SyntheticFixture.BuildAsync(_ws);
        var work = await SyntheticFixture.CloneAsync(_ws, built, "ls");
        var before = File.ReadAllLines(Path.Combine(work, "src/Domain/Money.cs"));
        await MutationApplier.ApplyAsync(work, SyntheticFixture.MutationDir("line-shift"));
        var after = File.ReadAllLines(Path.Combine(work, "src/Domain/Money.cs"));

        Assert.Equal(before.Length + 2, after.Length);
        Assert.Equal(before, after.Skip(2));
    }

    [Fact]
    public async Task Rename_and_edit_changes_both_the_path_and_the_content()
    {
        var built = await SyntheticFixture.BuildAsync(_ws);
        var work = await SyntheticFixture.CloneAsync(_ws, built, "re");
        var result = await MutationApplier.ApplyAsync(work, SyntheticFixture.MutationDir("rename-and-edit"));

        Assert.Equal(["src/Services/Formatting/OrderFormatter.cs", "src/Services/OrderFormatter.cs"], result.ChangedPaths);
        Assert.NotEqual(
            await Git.RunAsync(work, ["rev-parse", $"{built.Commit}:src/Services/OrderFormatter.cs"]),
            await Git.RunAsync(work, ["rev-parse", "HEAD:src/Services/Formatting/OrderFormatter.cs"]));
    }

    [Fact]
    public async Task Applying_on_a_different_head_is_MUT_BASE_MISMATCH()
    {
        var built = await SyntheticFixture.BuildAsync(_ws);
        var work = await SyntheticFixture.CloneAsync(_ws, built, "bm");
        await MutationApplier.ApplyAsync(work, SyntheticFixture.MutationDir("add-file"));

        var ex = await Assert.ThrowsAsync<FixturePreparationException>(
            () => MutationApplier.ApplyAsync(work, SyntheticFixture.MutationDir("edit-method")));
        Assert.Equal("MUT_BASE_MISMATCH", ex.Code);
    }

    [Fact]
    public async Task A_declared_base_sha_is_honoured()
    {
        var built = await SyntheticFixture.BuildAsync(_ws);
        var work = await SyntheticFixture.CloneAsync(_ws, built, "sha");
        var dir = WriteMutation("by-sha", built.Commit, new { op = "write", path = "x.txt", payloadFile = "x.txt" }, ("x.txt", "x"));

        var result = await MutationApplier.ApplyAsync(work, dir);
        Assert.Equal(["x.txt"], result.ChangedPaths);

        var wrong = WriteMutation("wrong-sha", new string('0', 40), new { op = "write", path = "y.txt", payloadFile = "y.txt" }, ("y.txt", "y"));
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() => MutationApplier.ApplyAsync(work, wrong));
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
        var built = await SyntheticFixture.BuildAsync(_ws);
        var work = await SyntheticFixture.CloneAsync(_ws, built, "bad");
        var dir = WriteMutation("bad", "initial", new { op = "write", path, payloadFile = "p.txt" }, ("p.txt", "p"));

        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() => MutationApplier.ApplyAsync(work, dir));

        Assert.Equal("MUT_BAD_PATH", ex.Code);
        Assert.False(File.Exists(_ws.Combine("work", "outside.txt")));
        Assert.Equal("", await Git.RunAsync(work, ["status", "--porcelain"]));
    }

    [Fact]
    public async Task Payload_escaping_the_mutation_directory_is_MUT_BAD_PATH()
    {
        var built = await SyntheticFixture.BuildAsync(_ws);
        var work = await SyntheticFixture.CloneAsync(_ws, built, "pl");
        File.WriteAllText(_ws.Combine("secret.txt"), "s");
        var dir = WriteMutation("pl", "initial", new { op = "write", path = "x.txt", payloadFile = "../secret.txt" });

        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() => MutationApplier.ApplyAsync(work, dir));
        Assert.Equal("MUT_BAD_PATH", ex.Code);
    }

    [Fact]
    public async Task Unknown_op_or_missing_definition_is_MUT_INVALID()
    {
        var built = await SyntheticFixture.BuildAsync(_ws);
        var work = await SyntheticFixture.CloneAsync(_ws, built, "inv");

        var unknown = WriteMutation("unknown", "initial", new { op = "explode", path = "x" });
        Assert.Equal("MUT_INVALID", (await Assert.ThrowsAsync<FixturePreparationException>(() => MutationApplier.ApplyAsync(work, unknown))).Code);

        Directory.CreateDirectory(_ws.Combine("empty"));
        Assert.Equal("MUT_INVALID", (await Assert.ThrowsAsync<FixturePreparationException>(() => MutationApplier.ApplyAsync(work, _ws.Combine("empty")))).Code);

        var missing = WriteMutation("missing", "initial", new { op = "delete", path = "no/such/file.cs" });
        Assert.Equal("MUT_INVALID", (await Assert.ThrowsAsync<FixturePreparationException>(() => MutationApplier.ApplyAsync(work, missing))).Code);
    }

    private string WriteMutation(string name, string @base, object op, params (string File, string Text)[] payloads)
    {
        var dir = _ws.Combine("mutations", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mutation.json"), JsonSerializer.Serialize(new { name, @base, ops = new[] { op } }));
        foreach (var (file, text) in payloads) File.WriteAllText(Path.Combine(dir, file), text);
        return dir;
    }
}

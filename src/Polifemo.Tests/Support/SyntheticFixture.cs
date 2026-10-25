using System.Text.RegularExpressions;
using Polifemo.Platform.Fixtures;

namespace Polifemo.Tests.Support;

/// <summary>Builds the committed synthetic fixture into a test workspace and prepares clones of it.</summary>
public static partial class SyntheticFixture
{
    [GeneratedRegex(@"^Commit:\s*`?([0-9a-f]{40})`?\s*$", RegexOptions.Multiline)]
    private static partial Regex RecordedCommitLine();

    public static string Directory => System.IO.Path.Combine(TestWorkspace.RepoRoot(), "src", "fixtures", "synthetic-csharp");

    public static string MutationDir(string name) => System.IO.Path.Combine(Directory, "mutations", name);

    public static IReadOnlyList<string> MutationNames() =>
        System.IO.Directory.GetDirectories(System.IO.Path.Combine(Directory, "mutations"))
            .Select(System.IO.Path.GetFileName).Cast<string>().Order(StringComparer.Ordinal).ToList();

    public static TheoryData<string> MutationData()
    {
        var data = new TheoryData<string>();
        foreach (var n in MutationNames()) data.Add(n);
        return data;
    }

    public static Task<BuiltFixture> BuildAsync(TestWorkspace ws, string name = "source") =>
        FixtureBuilder.BuildAsync(Directory, ws.Combine("fixtures", name));

    private static readonly Lazy<Task<BuiltFixture>> Shared = new(BuildSharedAsync);

    /// <summary>
    /// The fixture repository, built once per test process. Clones only read it, so tests can share it;
    /// use <see cref="BuildAsync"/> when a test needs a private copy or tests the build itself.
    /// </summary>
    public static Task<BuiltFixture> SharedAsync() => Shared.Value;

    private static async Task<BuiltFixture> BuildSharedAsync()
    {
        var root = System.IO.Path.Combine(TestWorkspace.RepoRoot(), ".index", "test", "_shared", Environment.ProcessId.ToString());
        var built = await FixtureBuilder.BuildAsync(Directory, System.IO.Path.Combine(root, "synthetic-csharp"));
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { System.IO.Directory.Delete(root, recursive: true); } catch (IOException) { }
        };
        return built;
    }

    public static async Task<string> CloneAsync(TestWorkspace ws, BuiltFixture built, string name, bool index = false)
    {
        var prepared = await new SamplePreparer(ws.Combine("work")).PrepareAsync(built.RepositoryPath, built.Commit, name, index);
        return prepared.WorkDir;
    }

    /// <summary>The commit recorded in PROVENANCE.md (the line <c>Commit: &lt;40 hex&gt;</c>).</summary>
    public static string RecordedCommit()
    {
        var text = System.IO.File.ReadAllText(System.IO.Path.Combine(Directory, "PROVENANCE.md"));
        var m = RecordedCommitLine().Match(text);
        return m.Success ? m.Groups[1].Value : throw new InvalidOperationException("PROVENANCE.md has no 'Commit: <sha>' line.");
    }
}

/// <summary>A fixture state: the base commit, or the base with one mutation applied, optionally indexed.</summary>
public sealed record FixtureState(string Name, string Worktree, string Commit);

public static class FixtureStates
{
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string> { "base" };
        foreach (var n in SyntheticFixture.MutationNames()) data.Add(n);
        return data;
    }

    public static async Task<FixtureState> CreateAsync(TestWorkspace ws, string state, bool index)
    {
        var built = await SyntheticFixture.SharedAsync();
        var work = await SyntheticFixture.CloneAsync(ws, built, state);
        var commit = built.Commit;
        if (state != "base")
            commit = (await Polifemo.Platform.Fixtures.MutationApplier.ApplyAsync(work, SyntheticFixture.MutationDir(state))).Commit!;
        if (index)
        {
            var init = await Polifemo.Platform.Processes.ProcessRunner.RunAsync(
                "tokensave", ["init", work], work, new Dictionary<string, string> { ["GIT_CONFIG_GLOBAL"] = "/dev/null", ["GIT_CONFIG_NOSYSTEM"] = "1" });
            if (init.ExitCode != 0) throw new InvalidOperationException($"tokensave init failed: {init.StdErr}");
        }
        return new FixtureState(state, work, commit);
    }
}

using System.CommandLine;
using Polifemo.Platform.Fixtures;

namespace Polifemo.Cli;

public static class FixtureCommands
{
    public static Command Create(TextWriter stdout, TextWriter stderr, Func<string>? workRoot = null)
    {
        workRoot ??= RepositoryPaths.WorkRoot;
        var fixture = new Command("fixture", "Build, prepare and mutate test fixtures.");
        fixture.Subcommands.Add(Build(stdout, stderr));
        fixture.Subcommands.Add(Prepare(stdout, stderr, workRoot));
        fixture.Subcommands.Add(Worktree(stdout, stderr));
        fixture.Subcommands.Add(Mutate(stdout, stderr));
        return fixture;
    }

    private static Command Build(TextWriter stdout, TextWriter stderr)
    {
        var dir = new Option<string>("--fixture") { Description = "Fixture folder with src/, docs/, tokensave-docs/.", Required = true };
        var outDir = new Option<string>("--out") { Description = "Output repository folder inside .index/.", Required = true };
        var command = new Command("build", "Turn plain fixture files into a deterministic git repository.") { dir, outDir };
        command.SetAction((parse, ct) => CliOutput.RunAsync(stdout, stderr, async () =>
        {
            var built = await FixtureBuilder.BuildAsync(parse.GetValue(dir)!, parse.GetValue(outDir)!, ct);
            return (ExitCodes.Ok, (object)new { commit = built.Commit, treeSha = built.TreeSha });
        }));
        return command;
    }

    private static Command Prepare(TextWriter stdout, TextWriter stderr, Func<string> workRoot)
    {
        var sample = new Option<string>("--sample") { Description = "Sample repository (read only).", Required = true };
        var commit = new Option<string>("--commit") { Description = "Commit to check out.", Required = true };
        var name = new Option<string>("--name") { Description = "Work directory name under .index/work/.", Required = true };
        var index = new Option<bool>("--index") { Description = "Also run the pinned `tokensave init` in the clone." };
        var replace = new Option<bool>("--replace") { Description = "Replace an existing work directory." };
        var command = new Command("prepare", "Clone a sample at a commit into .index/work/<name>/.") { sample, commit, name, index, replace };
        command.SetAction((parse, ct) => CliOutput.RunAsync(stdout, stderr, async () =>
        {
            var prepared = await new SamplePreparer(workRoot()).PrepareAsync(
                parse.GetValue(sample)!, parse.GetValue(commit)!, parse.GetValue(name)!,
                parse.GetValue(index), parse.GetValue(replace), ct);
            return (ExitCodes.Ok, (object)new
            {
                workDir = prepared.WorkDir,
                commit = prepared.Commit,
                sourceUnchanged = prepared.SourceUnchanged,
                indexed = prepared.Indexed,
            });
        }));
        return command;
    }

    private static Command Worktree(TextWriter stdout, TextWriter stderr)
    {
        var work = new Option<string>("--work") { Description = "Prepared clone.", Required = true };
        var name = new Option<string>("--name") { Description = "Name of the new worktree.", Required = true };
        var dirty = new Option<string?>("--dirty-from") { Description = "Mutation folder or name to apply without committing." };
        var command = new Command("worktree", "Add a second worktree inside the clone's repository.") { work, name, dirty };
        command.SetAction((parse, ct) => CliOutput.RunAsync(stdout, stderr, async () =>
        {
            var created = await WorktreeFactory.AddAsync(parse.GetValue(work)!, parse.GetValue(name)!, MutationPath(parse.GetValue(dirty)), ct);
            return (ExitCodes.Ok, (object)new { worktree = created.Path, dirty = created.Dirty, changedPaths = created.ChangedPaths });
        }));
        return command;
    }

    private static Command Mutate(TextWriter stdout, TextWriter stderr)
    {
        var worktree = new Option<string>("--worktree") { Description = "Worktree to mutate.", Required = true };
        var mutation = new Option<string>("--mutation") { Description = "Mutation folder, or a mutation name of the synthetic fixture.", Required = true };
        var noCommit = new Option<bool>("--no-commit") { Description = "Leave the changes uncommitted." };
        var command = new Command("mutate", "Apply a named mutation byte-exactly.") { worktree, mutation, noCommit };
        command.SetAction((parse, ct) => CliOutput.RunAsync(stdout, stderr, async () =>
        {
            var result = await MutationApplier.ApplyAsync(
                parse.GetValue(worktree)!, MutationPath(parse.GetValue(mutation))!, !parse.GetValue(noCommit), ct);
            return (ExitCodes.Ok, (object)new
            {
                @base = result.Base,
                commit = result.Commit,
                treeSha = result.TreeSha,
                changedPaths = result.ChangedPaths,
            });
        }));
        return command;
    }

    /// <summary>A bare name means <c>src/fixtures/synthetic-csharp/mutations/&lt;name&gt;</c>.</summary>
    private static string? MutationPath(string? value)
    {
        if (value is null) return null;
        var isName = !value.Contains('/') && !value.Contains('\\') && !Directory.Exists(value);
        return isName
            ? Path.Combine(RepositoryPaths.Root(), "src", "fixtures", "synthetic-csharp", "mutations", value)
            : value;
    }
}

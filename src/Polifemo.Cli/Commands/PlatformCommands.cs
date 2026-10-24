using System.CommandLine;
using Polifemo.Platform;
using Polifemo.Platform.Elasticsearch;
using Polifemo.Platform.TokenSave;

namespace Polifemo.Cli;

public static class PlatformCommands
{
    public static Command Create(TextWriter stdout, TextWriter stderr, Func<CancellationToken, Task<string>>? binaryVersion)
    {
        var platform = new Command("platform", "Verify the pinned platform.");
        platform.Subcommands.Add(Check(stdout, stderr, binaryVersion));
        platform.Subcommands.Add(Smoke(stdout, stderr));
        return platform;
    }

    private static Command Check(TextWriter stdout, TextWriter stderr, Func<CancellationToken, Task<string>>? binaryVersion)
    {
        var worktree = new Option<string>("--worktree") { Description = "Worktree whose .tokensave/ is checked.", Required = true };
        var expect = new Option<string>("--expect-tokensave")
        {
            Description = "Expected TokenSave version.",
            DefaultValueFactory = _ => PlatformPins.TokenSave,
        };
        var command = new Command("check", "Compare TokenSave and its database with the pinned expectation.") { worktree, expect };
        command.SetAction((parse, ct) => CliOutput.RunAsync(stdout, stderr, async () =>
        {
            var dir = parse.GetValue(worktree)!;
            if (!Directory.Exists(dir)) throw new UsageException($"Worktree '{dir}' does not exist.");

            var checker = binaryVersion is null
                ? new CompatibilityChecker()
                : new CompatibilityChecker(new ActiveDatabaseResolver(), binaryVersion);
            var report = await checker.CheckAsync(dir, parse.GetValue(expect)!, ct);
            var provenance = await ResultProvenance.ForWorktreeAsync(dir, report.TokensaveBinaryVersion ?? "unknown", ct: ct);
            return (report.Ok ? ExitCodes.Ok : ExitCodes.CheckFailed, (object)new
            {
                ok = report.Ok,
                report.TokensaveBinaryVersion,
                report.IndexVersion,
                report.DatabasePath,
                report.Resolution,
                report.Mismatches,
                report.Warnings,
                provenance,
            });
        }));
        return command;
    }

    private static Command Smoke(TextWriter stdout, TextWriter stderr)
    {
        var url = new Option<string?>("--url") { Description = "Defaults to ELASTICSEARCH_URL." };
        var username = new Option<string?>("--username") { Description = "Defaults to ELASTICSEARCH_USERNAME." };
        var command = new Command("smoke", "Run BM25 and knn on synthetic documents; Basic authentication is optional.") { url, username };
        command.SetAction((parse, ct) => CliOutput.RunAsync(stdout, stderr, async () =>
        {
            var options = SmokeOptions.FromEnvironment(parse.GetValue(url), parse.GetValue(username));
            var report = await new SmokeTest().RunAsync(options, ct);
            return (report.Ok ? ExitCodes.Ok : ExitCodes.CheckFailed, (object)new
            {
                ok = report.Ok,
                serverVersion = report.ServerVersion,
                license = report.License,
                checks = report.Checks,
            });
        }));
        return command;
    }
}

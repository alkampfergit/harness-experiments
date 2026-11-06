using System.CommandLine;
using Polifemo.Contracts.Errors;
using Polifemo.Platform;
using Polifemo.Platform.Elasticsearch;
using Polifemo.Platform.Privacy;
using Polifemo.Platform.TokenSave;

namespace Polifemo.Cli;

public static class PlatformCommands
{
    public static Command Create(TextWriter stdout, TextWriter stderr, Func<CancellationToken, Task<string>>? binaryVersion)
    {
        var platform = new Command("platform", "Verify the pinned platform.");
        platform.Subcommands.Add(Check(stdout, stderr, binaryVersion));
        platform.Subcommands.Add(Snapshot(stdout, stderr, binaryVersion));
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

    /// <summary>
    /// Reads one verified snapshot through <see cref="TokenSaveAdapter"/> and prints counts, revision and provenance,
    /// never row content (research R9). Typed TokenSave failures print <c>{ ok:false, code, message }</c> and exit 1.
    /// </summary>
    private static Command Snapshot(TextWriter stdout, TextWriter stderr, Func<CancellationToken, Task<string>>? binaryVersion)
    {
        var worktree = new Option<string>("--worktree") { Description = "Indexed worktree whose .tokensave/ is read.", Required = true };
        var noVerify = new Option<bool>("--no-verify-source") { Description = "Skip pairing the graph with the worktree bytes." };
        var lockTimeout = new Option<int>("--lock-timeout")
        {
            Description = "Seconds to wait for a TokenSave writer's lock.",
            DefaultValueFactory = _ => 5,
        };
        var command = new Command("snapshot", "Read one consistent, verified TokenSave snapshot and print its summary.") { worktree, noVerify, lockTimeout };
        command.SetAction((parse, ct) => CliOutput.RunAsync(stdout, stderr, async () =>
        {
            var dir = parse.GetValue(worktree)!;
            if (!Directory.Exists(dir)) throw new UsageException($"Worktree '{dir}' does not exist.");
            var seconds = parse.GetValue(lockTimeout);
            if (seconds < 1) throw new UsageException("--lock-timeout must be at least 1 second.");

            var checker = binaryVersion is null
                ? new CompatibilityChecker()
                : new CompatibilityChecker(new ActiveDatabaseResolver(), binaryVersion);
            var options = new SnapshotReadOptions { LockTimeout = TimeSpan.FromSeconds(seconds), VerifySource = !parse.GetValue(noVerify) };
            GraphSnapshot snapshot;
            try
            {
                snapshot = await new TokenSaveAdapter(checker).ReadAsync(dir, options, ct);
            }
            catch (PolifemoException ex) when (ex is ActiveDatabaseResolutionException or TokenSaveCompatibilityException or TokenSaveSnapshotException)
            {
                stderr.WriteLine($"{ex.Code}: {SafeMessage.For(ex)}");
                return (ExitCodes.CheckFailed, (object)new
                {
                    ok = false,
                    code = ex.Code,
                    message = SafeMessage.For(ex),
                    mismatches = (ex as TokenSaveCompatibilityException)?.Mismatches,
                });
            }

            var provenance = await ResultProvenance.ForWorktreeAsync(dir, snapshot.Provenance.BinaryVersion ?? "unknown", ct: ct);
            var graph = snapshot.Graph;
            return (ExitCodes.Ok, (object)new
            {
                ok = true,
                revision = snapshot.Revision.Value,
                sourceFingerprint = snapshot.Source.Value,
                counts = new
                {
                    files = graph.Files.Count,
                    symbols = graph.Symbols.Count,
                    edges = graph.Edges.Count,
                    unresolved = graph.Unresolved.Count,
                    ambiguous = graph.Ambiguous.Count,
                    docAssociations = snapshot.DocAssociations.Count,
                },
                integrity = snapshot.Integrity,
                tokensave = snapshot.Provenance,
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
            var report = await SmokeTest.RunAsync(options, ct);
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

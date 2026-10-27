using Polifemo.Contracts.Errors;
using Polifemo.Platform.Processes;

namespace Polifemo.Platform.Fixtures;

/// <summary>
/// Runs git with the user's and the system's configuration disabled, so that global hooks (for
/// example the TokenSave ones), templates and signing never influence a fixture or a sample.
/// </summary>
internal static class Git
{
    public static readonly IReadOnlyDictionary<string, string> IsolatedEnvironment = new Dictionary<string, string>
    {
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_CONFIG_GLOBAL"] = "/dev/null",
        ["GIT_TERMINAL_PROMPT"] = "0",
    };

    public static IReadOnlyDictionary<string, string> WithDate(string isoDate) =>
        new Dictionary<string, string>(IsolatedEnvironment)
        {
            ["GIT_AUTHOR_NAME"] = "Polifemo Fixture",
            ["GIT_AUTHOR_EMAIL"] = "fixture@polifemo.invalid",
            ["GIT_COMMITTER_NAME"] = "Polifemo Fixture",
            ["GIT_COMMITTER_EMAIL"] = "fixture@polifemo.invalid",
            ["GIT_AUTHOR_DATE"] = isoDate,
            ["GIT_COMMITTER_DATE"] = isoDate,
        };

    public static Task<ProcessResult> TryAsync(
        string? workDir, IEnumerable<string> args, IReadOnlyDictionary<string, string>? env = null, CancellationToken ct = default) =>
        ProcessRunner.RunAsync("git", args, workDir, env ?? IsolatedEnvironment, ct: ct);

    /// <summary>Runs git and returns trimmed stdout. A failure is a typed <c>GIT_FAILED</c> error.</summary>
    public static async Task<string> RunAsync(
        string? workDir, IEnumerable<string> args, IReadOnlyDictionary<string, string>? env = null, CancellationToken ct = default)
    {
        var list = args.ToList();
        var result = await TryAsync(workDir, list, env, ct);
        return result.ExitCode == 0
            ? result.StdOut.Trim()
            : throw new FixturePreparationException("GIT_FAILED", $"git {list.FirstOrDefault()} failed with exit code {result.ExitCode}.");
    }
}

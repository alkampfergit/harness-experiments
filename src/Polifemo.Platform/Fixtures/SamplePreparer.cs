using System.Text.Json;
using System.Text.RegularExpressions;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.TokenSave;

namespace Polifemo.Platform.Fixtures;

public sealed record PreparedSample(string WorkDir, string Commit, bool SourceUnchanged, bool Indexed);

/// <summary>
/// Clones a sample at a commit into <c>.index/work/&lt;name&gt;/</c> without touching the source
/// (research R5). A local <c>git clone</c> only reads the source, whereas <c>git worktree add</c>
/// would write into its <c>.git</c>.
/// </summary>
public sealed partial class SamplePreparer(string workRoot)
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex NamePattern();

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public async Task<PreparedSample> PrepareAsync(
        string samplePath, string commit, string name, bool index = false, bool replace = false, CancellationToken ct = default)
    {
        workRoot = Path.GetFullPath(workRoot);
        var target = ResolveTarget(name);

        samplePath = Path.GetFullPath(samplePath);
        if (FixtureBuilder.Overlaps(samplePath, target) || FixtureBuilder.Overlaps(samplePath, target + ".tmp")
            || FixtureBuilder.Overlaps(samplePath, Path.Combine(workRoot, ".empty-hooks")))
            throw new FixturePreparationException("PREP_BAD_NAME", $"'{target}' overlaps the sample '{samplePath}'.");
        await EnsureRepositoryAsync(samplePath, ct);
        var sha = await ResolveCommitAsync(samplePath, commit, ct);

        if (Directory.Exists(target) && !replace)
            throw new FixturePreparationException("PREP_TARGET_EXISTS", $"'{target}' already exists; use --replace.");

        if (index) await RequirePinnedTokenSaveAsync(ct);

        var before = await CaptureAsync(samplePath, ct);
        await CloneAsync(samplePath, sha, target, ct);

        var after = await CaptureAsync(samplePath, ct);
        if (before != after)
        {
            Directory.Delete(target, recursive: true);
            throw new FixturePreparationException("PREP_SOURCE_MODIFIED", $"The sample checkout '{samplePath}' changed during preparation.");
        }

        if (index)
        {
            var init = await Processes.ProcessRunner.RunAsync("tokensave", ["init", target], target, Git.IsolatedEnvironment, ct: ct);
            if (init.ExitCode != 0)
                throw new FixturePreparationException("PREP_INDEX_FAILED", $"tokensave init failed: {init.StdErr.Trim()}");
        }

        await File.WriteAllTextAsync(Path.Combine(target, ".polifemo-prep.json"), JsonSerializer.Serialize(new
        {
            source = samplePath,
            commit = sha,
            tokensave = PlatformPins.TokenSave,
            preparedAtUtc = DateTime.UtcNow.ToString("O"),
        }, IndentedJson), ct);

        return new PreparedSample(target, sha, SourceUnchanged: true, Indexed: index);
    }

    private string ResolveTarget(string name)
    {
        if (!NamePattern().IsMatch(name) || name.EndsWith(".tmp", StringComparison.Ordinal))
            throw new FixturePreparationException("PREP_BAD_NAME", $"'{name}' is not a valid work directory name.");
        var target = Path.GetFullPath(Path.Combine(workRoot, name));
        if (!target.StartsWith(workRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new FixturePreparationException("PREP_BAD_NAME", $"'{name}' resolves outside the work directory.");
        return target;
    }

    /// <summary>Clones into <c>&lt;target&gt;.tmp</c>, checks out the commit and moves the result into place.</summary>
    private async Task CloneAsync(string samplePath, string sha, string target, CancellationToken ct)
    {
        var temp = target + ".tmp";
        var hooks = Path.Combine(workRoot, ".empty-hooks");
        Directory.CreateDirectory(hooks);
        if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);

        try
        {
            await Git.RunAsync(null,
                ["-c", "core.autocrlf=false", "-c", $"core.hooksPath={hooks}",
                 "clone", "-q", "--no-hardlinks", "--no-checkout", samplePath, temp], ct: ct);
            await Git.RunAsync(temp, ["config", "core.hooksPath", hooks], ct: ct);
            await Git.RunAsync(temp, ["config", "core.autocrlf", "false"], ct: ct);
            await Git.RunAsync(temp, ["checkout", "-q", "--detach", sha], ct: ct);
            await File.AppendAllTextAsync(Path.Combine(temp, ".git", "info", "exclude"),
                "\n.polifemo-prep.json\n.tokensave/\n", ct);

            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(temp, target);
        }
        catch
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            throw;
        }
    }

    private static async Task EnsureRepositoryAsync(string path, CancellationToken ct)
    {
        var ok = Directory.Exists(path)
            && (await Git.TryAsync(path, ["rev-parse", "--show-cdup"], ct: ct)) is { ExitCode: 0, StdOut: var cdup }
            && cdup.Trim().Length == 0;
        if (!ok) throw new FixturePreparationException("PREP_NOT_A_REPO", $"'{path}' is not the root of a git repository.");
    }

    private static async Task<string> ResolveCommitAsync(string repo, string commit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(commit) || commit.StartsWith('-'))
            throw new FixturePreparationException("PREP_UNKNOWN_COMMIT", $"'{commit}' is not a commit.");
        var r = await Git.TryAsync(repo, ["rev-parse", "--verify", "--quiet", $"{commit}^{{commit}}"], ct: ct);
        return r.ExitCode == 0
            ? r.StdOut.Trim()
            : throw new FixturePreparationException("PREP_UNKNOWN_COMMIT", $"Commit '{commit}' does not exist in '{repo}'.");
    }

    private static async Task RequirePinnedTokenSaveAsync(CancellationToken ct)
    {
        var version = await TokenSaveBinary.VersionAsync(ct);
        if (version != PlatformPins.TokenSave)
            throw new TokenSaveCompatibilityException([new Mismatch("TS_VERSION_MISMATCH", "tokensave --version", PlatformPins.TokenSave, version)]);
    }

    /// <summary>HEAD plus full status including ignored files; any difference means the source was touched.</summary>
    private static async Task<string> CaptureAsync(string repo, CancellationToken ct)
    {
        var head = await Git.RunAsync(repo, ["rev-parse", "HEAD"], ct: ct);
        var status = await Git.RunAsync(repo, ["--no-optional-locks", "status", "--porcelain=v1", "--ignored", "--untracked-files=all"], ct: ct);
        return head + "\n" + status;
    }
}

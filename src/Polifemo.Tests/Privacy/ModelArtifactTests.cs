using Polifemo.Contracts.Errors;
using Polifemo.Platform.Privacy;

namespace Polifemo.Tests.Privacy;

public sealed class ModelArtifactTests : IDisposable
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private static readonly string[] BadConfigs = ["""{"report_savings": true}""", "{}", "[]"];
    private static readonly string[] TrackedConfigs = [".devcontainer/docker-compose.yml", ".devcontainer/devcontainer.json"];
    private readonly string _root = Directory.CreateTempSubdirectory("polifemo-models").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ModelReview Review(bool remote = false, string? remoteRevision = null, string license = "MIT") =>
        new("org/model", Sha, license, remote, remoteRevision, "reviewer", "2026-10-26");

    private string Acquire()
    {
        var dir = ModelArtifacts.DirectoryFor(_root, "org/model", Sha);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config.json"), "{}");
        return dir;
    }

    [Fact]
    public void A_missing_artifact_gives_an_actionable_error_and_downloads_nothing()
    {
        var ex = Assert.Throws<ModelArtifactException>(() => ModelArtifacts.Require("org/model", Sha, _root, [Review()]));

        Assert.Equal("MODEL_ARTIFACT_MISSING", ex.Code);
        Assert.Contains("never downloaded at run time", ex.Message);
        Assert.Contains("docs/local-only.md", ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void A_reviewed_and_present_model_is_accepted() =>
        Assert.Equal(Acquire(), ModelArtifacts.Require("org/model", Sha, _root, [Review()]));

    [Fact]
    public void A_model_without_a_review_is_refused_even_when_present()
    {
        Acquire();

        Assert.Equal("MODEL_NOT_REVIEWED", Assert.Throws<ModelArtifactException>(() => ModelArtifacts.Require("org/model", Sha, _root, [])).Code);
    }

    [Fact]
    public void A_moving_revision_is_refused() =>
        Assert.Equal("MODEL_REVISION_NOT_PINNED", Assert.Throws<ModelArtifactException>(() => ModelArtifacts.Require("org/model", "main", _root, [Review()])).Code);

    [Fact]
    public void A_review_without_a_license_is_incomplete()
    {
        Acquire();

        Assert.Equal("MODEL_REVIEW_INCOMPLETE", Assert.Throws<ModelArtifactException>(() => ModelArtifacts.Require("org/model", Sha, _root, [Review(license: " ")])).Code);
    }

    [Fact]
    public void Remote_model_code_needs_a_pinned_reviewed_revision()
    {
        Acquire();

        Assert.Equal("MODEL_REMOTE_CODE_UNREVIEWED", Assert.Throws<ModelArtifactException>(() => ModelArtifacts.Require("org/model", Sha, _root, [Review(remote: true)])).Code);
        Assert.Equal("MODEL_REMOTE_CODE_UNREVIEWED", Assert.Throws<ModelArtifactException>(() => ModelArtifacts.Require("org/model", Sha, _root, [Review(remote: true, remoteRevision: "main")])).Code);
        ModelArtifacts.Require("org/model", Sha, _root, [Review(remote: true, remoteRevision: Sha)]);
    }

    [Fact]
    public void Review_files_are_loaded_and_validated()
    {
        var file = Path.Combine(_root, "reviews.json");

        Assert.Equal("MODEL_REVIEW_MISSING", Assert.Throws<ModelArtifactException>(() => ModelArtifacts.LoadReviews(file)).Code);
        File.WriteAllText(file, "{ nope");
        Assert.Equal("MODEL_REVIEW_INVALID", Assert.Throws<ModelArtifactException>(() => ModelArtifacts.LoadReviews(file)).Code);
        File.WriteAllText(file, $$"""[{"model":"org/model","revision":"{{Sha}}","license":"MIT","requiresRemoteCode":false,"reviewedBy":"r","reviewedOn":"2026-10-26"}]""");
        Assert.Single(ModelArtifacts.LoadReviews(file));
    }

    [Fact]
    public async Task The_cli_reports_a_missing_artifact_with_exit_1()
    {
        var file = Path.Combine(_root, "reviews.json");
        File.WriteAllText(file, $$"""[{"model":"org/model","revision":"{{Sha}}","license":"MIT","requiresRemoteCode":false,"reviewedBy":"r","reviewedOn":"2026-10-26"}]""");
        var args = new[] { "privacy", "verify-model", "--model", "org/model", "--revision", Sha, "--reviews", file, "--model-dir", Path.Combine(_root, "models") };

        var missing = new StringWriter();
        Assert.Equal(1, await Polifemo.Cli.CliApp.RunAsync(args, missing, new StringWriter(), ct: TestContext.Current.CancellationToken));
        Assert.Contains("MODEL_ARTIFACT_MISSING", missing.ToString());

        var dir = ModelArtifacts.DirectoryFor(Path.Combine(_root, "models"), "org/model", Sha);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config.json"), "{}");
        Assert.Equal(0, await Polifemo.Cli.CliApp.RunAsync(args, new StringWriter(), new StringWriter(), ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TokenSave_uploads_must_be_verifiably_disabled()
    {
        Assert.Equal("LOCAL_ONLY_TOKENSAVE_CONFIG_MISSING", Assert.Throws<LocalOnlyPolicyException>(() => TokenSaveUploads.EnsureDisabled(_root)).Code);
        Directory.CreateDirectory(Path.Combine(_root, ".tokensave"));
        var config = Path.Combine(_root, ".tokensave", "config.json");
        foreach (var bad in BadConfigs)
        {
            File.WriteAllText(config, bad);
            Assert.Equal("LOCAL_ONLY_TOKENSAVE_UPLOAD", Assert.Throws<LocalOnlyPolicyException>(() => TokenSaveUploads.EnsureDisabled(_root)).Code);
        }
        File.WriteAllText(config, """{"report_savings": false}""");
        TokenSaveUploads.EnsureDisabled(_root);
    }

    [Fact]
    public async Task Credentials_do_not_appear_in_tracked_configuration()
    {
        var root = Polifemo.Tests.Support.TestWorkspace.RepoRoot();
        var tracked = TrackedConfigs
            .Select(f => Path.Combine(root, f)).Where(File.Exists);
        foreach (var file in tracked)
        {
            var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
            Assert.DoesNotMatch(@"(?i)(api[_-]?key|token|secret)\s*[:=]\s*[A-Za-z0-9]{16,}", text);
        }
        Assert.Contains(".env", await File.ReadAllTextAsync(Path.Combine(root, ".gitignore"), TestContext.Current.CancellationToken));
    }
}

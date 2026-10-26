using Polifemo.Contracts.Errors;
using Polifemo.Contracts.Profiles;
using Polifemo.Platform.Privacy;
using Polifemo.Platform.Processes;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Privacy;

public sealed class ModelAndEnvironmentTests : IDisposable
{
    private const string Revision = "0123456789abcdef0123456789abcdef01234567";
    private static readonly string[] TrackedConfigFiles = [".tokensave/config.json", ".devcontainer/docker-compose.yml"];
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private static EmbeddingProfile Profile(string provider = "sentence-transformers") =>
        new(new EmbeddingModelRef(provider, "nomic-ai/nomic-embed-text-v1.5", Revision),
            new EmbeddingPrefixes("search_document: ", "search_query: "), "nomic-bert", 8192, 768, "mean", true);

    private static ModelApproval Approval(bool remoteCode = false, bool reviewed = false, string license = "Apache-2.0") =>
        new("nomic-ai/nomic-embed-text-v1.5", Revision, license, remoteCode, reviewed, "2026-10-26");

    private void Acquire()
    {
        var path = ModelArtifactGate.ArtifactPath(_ws.Path, "nomic-ai/nomic-embed-text-v1.5", Revision);
        Directory.CreateDirectory(path);
        foreach (var file in new[] { "config.json", "modules.json", "tokenizer.json", "model.safetensors" })
            File.WriteAllText(Path.Combine(path, file), "{}");
    }

    private string Snapshot() => ModelArtifactGate.ArtifactPath(_ws.Path, "nomic-ai/nomic-embed-text-v1.5", Revision);

    [Fact]
    public void A_snapshot_with_only_config_json_is_incomplete()
    {
        Directory.CreateDirectory(Snapshot());
        File.WriteAllText(Path.Combine(Snapshot(), "config.json"), "{}");

        Assert.Equal("MODEL_ARTIFACT_MISSING", Code(new ModelArtifactGate(_ws.Path, [Approval()]), Profile()));
    }

    [Fact]
    public void An_empty_weight_file_or_dangling_link_is_incomplete()
    {
        Acquire();
        var weights = Path.Combine(Snapshot(), "model.safetensors");
        File.Delete(weights);
        File.CreateSymbolicLink(weights, Path.Combine(_ws.Path, "blobs-missing"));

        Assert.Equal("MODEL_ARTIFACT_MISSING", Code(new ModelArtifactGate(_ws.Path, [Approval()]), Profile()));

        File.Delete(weights);
        File.WriteAllBytes(weights, []);
        Assert.Equal("MODEL_ARTIFACT_MISSING", Code(new ModelArtifactGate(_ws.Path, [Approval()]), Profile()));
    }

    [Fact]
    public void Cache_links_that_resolve_inside_the_root_pass_and_links_leaving_it_do_not()
    {
        Acquire();
        var weights = Path.Combine(Snapshot(), "model.safetensors");
        File.Delete(weights);
        var blob = Path.Combine(_ws.Path, "blob");
        File.WriteAllText(blob, "w");
        File.CreateSymbolicLink(weights, blob);
        new ModelArtifactGate(_ws.Path, [Approval()]).EnsureReady(Profile());

        using var outside = new TestWorkspace();
        var elsewhere = Path.Combine(outside.Path, "w");
        File.WriteAllText(elsewhere, "w");
        File.Delete(weights);
        File.CreateSymbolicLink(weights, elsewhere);
        Assert.Equal("MODEL_ARTIFACT_MISSING", Code(new ModelArtifactGate(_ws.Path, [Approval()]), Profile()));
    }

    [Fact]
    public void A_snapshot_directory_linked_outside_the_root_is_incomplete()
    {
        Acquire();
        using var outside = new TestWorkspace();
        foreach (var file in Directory.GetFiles(Snapshot())) File.Copy(file, Path.Combine(outside.Path, Path.GetFileName(file)));
        Directory.Delete(Snapshot(), recursive: true);
        Directory.CreateSymbolicLink(Snapshot(), outside.Path);

        Assert.Equal("MODEL_ARTIFACT_MISSING", Code(new ModelArtifactGate(_ws.Path, [Approval()]), Profile()));
    }

    [Fact]
    public void A_root_with_a_trailing_separator_still_accepts_a_complete_model()
    {
        Acquire();

        Assert.Null(Record.Exception(() => new ModelArtifactGate(_ws.Path + Path.DirectorySeparatorChar, [Approval()]).EnsureReady(Profile())));
    }

    [Fact]
    public void The_manifest_depends_on_the_provider()
    {
        Assert.Contains(ModelArtifactGate.DefaultManifest("onnxruntime"), g => g.Contains("onnx/model.onnx"));
        Assert.Contains(ModelArtifactGate.DefaultManifest("sentence-transformers"), g => g.Contains("modules.json"));
    }

    private static string Code(ModelArtifactGate gate, EmbeddingProfile profile) =>
        Assert.Throws<PrivacyPolicyException>(() => gate.EnsureReady(profile)).Code;

    [Fact]
    public void Missing_artifacts_are_an_actionable_error_and_nothing_is_downloaded()
    {
        var gate = new ModelArtifactGate(_ws.Path, [Approval()]);

        var ex = Assert.Throws<PrivacyPolicyException>(() => gate.EnsureReady(Profile()));

        Assert.Equal("MODEL_ARTIFACT_MISSING", ex.Code);
        Assert.Contains("acquisition step", ex.Message);
        Assert.Contains("never download", ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_ws.Path));
    }

    [Fact]
    public void An_approved_and_acquired_model_passes()
    {
        Acquire();

        Assert.Null(Record.Exception(() => new ModelArtifactGate(_ws.Path, [Approval()]).EnsureReady(Profile())));
    }

    [Fact]
    public void A_model_without_a_review_record_or_license_is_not_enabled()
    {
        Acquire();

        Assert.Equal("MODEL_NOT_APPROVED", Code(new ModelArtifactGate(_ws.Path, []), Profile()));
        Assert.Equal("MODEL_NOT_APPROVED", Code(new ModelArtifactGate(_ws.Path, [Approval(license: " ")]), Profile()));
    }

    [Fact]
    public void Remote_code_needs_its_own_review()
    {
        Acquire();

        Assert.Equal("MODEL_REMOTE_CODE_UNREVIEWED", Code(new ModelArtifactGate(_ws.Path, [Approval(remoteCode: true)]), Profile()));
        new ModelArtifactGate(_ws.Path, [Approval(remoteCode: true, reviewed: true)]).EnsureReady(Profile());
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("cohere")]
    [InlineData("voyage")]
    public void Hosted_providers_are_refused(string provider) =>
        Assert.Equal("MODEL_PROVIDER_NOT_LOCAL", Code(new ModelArtifactGate(_ws.Path, [Approval()]), Profile(provider)));

    [Fact]
    public void Privacy_variables_cannot_be_overridden_by_the_caller()
    {
        var merged = PrivacyEnvironment.Merge(new Dictionary<string, string> { ["HF_HUB_OFFLINE"] = "0", ["KEEP"] = "yes" });

        Assert.Equal("1", merged["HF_HUB_OFFLINE"]);
        Assert.Equal("yes", merged["KEEP"]);
        Assert.Equal("true", merged["OTEL_SDK_DISABLED"]);
    }

    [Fact]
    public async Task Every_child_process_runs_offline_even_when_the_caller_asks_otherwise()
    {
        var result = await ProcessRunner.RunAsync("sh", ["-c", "echo $HF_HUB_OFFLINE-$DO_NOT_TRACK"],
            environment: new Dictionary<string, string> { ["HF_HUB_OFFLINE"] = "0" }, ct: TestContext.Current.CancellationToken);

        Assert.Equal("1-1", result.StdOut.Trim());
    }

    [Fact]
    public void The_local_tokensave_configuration_disables_uploads()
    {
        // .tokensave/config.json is git-ignored, so a fresh checkout or CI has none to check.
        var root = TestWorkspace.RepoRoot();
        Assert.SkipUnless(File.Exists(Path.Combine(root, ".tokensave", "config.json")),
            "No local .tokensave/config.json in this checkout.");
        Assert.Equal(UploadSetting.Disabled, TokenSaveUploadGuard.Read(root));
    }

    [Theory]
    [InlineData("""{"report_savings": true}""", UploadSetting.Enabled)]
    [InlineData("""{"report_savings": false}""", UploadSetting.Disabled)]
    [InlineData("""{"other": 1}""", UploadSetting.Unspecified)]
    [InlineData("""{"report_savings": "yes"}""", UploadSetting.Unspecified)]
    [InlineData("[]", UploadSetting.Unspecified)]
    [InlineData("not json", UploadSetting.Unspecified)]
    public void Upload_setting_is_read_from_the_project_configuration(string json, UploadSetting expected)
    {
        Directory.CreateDirectory(_ws.Combine(".tokensave"));
        File.WriteAllText(_ws.Combine(".tokensave", "config.json"), json);

        Assert.Equal(expected, TokenSaveUploadGuard.Read(_ws.Path));
    }

    [Fact]
    public void No_configuration_means_the_setting_is_unspecified() =>
        Assert.Equal(UploadSetting.Unspecified, TokenSaveUploadGuard.Read(_ws.Path));

    [Fact]
    public void Tracked_configuration_files_hold_no_credentials()
    {
        var root = TestWorkspace.RepoRoot();
        var files = TrackedConfigFiles
            .Select(f => Path.Combine(root, f)).Where(File.Exists);

        foreach (var file in files)
        {
            var hits = SecretRules.All.Where(r => r.Pattern.IsMatch(File.ReadAllText(file))).Select(r => r.Id);
            Assert.False(hits.Any(), $"{Path.GetFileName(file)} matches secret rule(s): {string.Join(", ", hits)}");
        }
    }

    [Fact]
    public void Payload_carrying_http_clients_are_created_only_by_LocalOnlyHttp()
    {
        var platform = Path.Combine(TestWorkspace.RepoRoot(), "src", "Polifemo.Platform");
        var offenders = Directory.EnumerateFiles(platform, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && Path.GetFileName(f) != "LocalOnlyHttp.cs"
                && File.ReadAllText(f).Contains("new HttpClient(", StringComparison.Ordinal))
            .Select(Path.GetFileName);

        Assert.Empty(offenders);
    }
}

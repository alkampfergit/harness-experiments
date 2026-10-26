using Polifemo.Contracts.Errors;
using Polifemo.Contracts.Profiles;

namespace Polifemo.Platform.Privacy;

/// <summary>
/// The review record that must exist before a model profile is enabled: license, revision and whether the
/// model needs remote code, which runs arbitrary code from the model repository.
/// </summary>
public sealed record ModelApproval(
    string Model, string Revision, string License, bool UsesRemoteCode, bool RemoteCodeReviewed, string ReviewedOn,
    IReadOnlyList<IReadOnlyList<string>>? Manifest = null);

/// <summary>
/// Model acquisition is a separate, explicit step (docs/privacy.md). At run time this gate only checks that the
/// reviewed, pinned artifacts are already on disk; it never downloads anything.
/// </summary>
public sealed class ModelArtifactGate(string artifactRoot, IReadOnlyList<ModelApproval> approvals)
{
    private static readonly string[] LocalProviders = ["sentence-transformers", "onnxruntime"];

    /// <summary>
    /// What a snapshot must contain, per provider: every group needs at least one of its alternative files. An approval
    /// may carry its own manifest for a model whose layout differs.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> DefaultManifest(string provider) =>
        provider.Equals("onnxruntime", StringComparison.OrdinalIgnoreCase)
            ? [["config.json"], ["tokenizer.json"], ["onnx/model.onnx", "model.onnx"]]
            : [["config.json"], ["modules.json"], ["tokenizer.json", "tokenizer_config.json"], ["model.safetensors", "pytorch_model.bin"]];

    /// <summary>Hugging Face cache layout: <c>models--org--name/snapshots/&lt;revision&gt;</c>.</summary>
    public static string ArtifactPath(string root, string model, string revision) =>
        Path.Combine(root, "models--" + model.Replace("/", "--", StringComparison.Ordinal), "snapshots", revision);

    public void EnsureReady(EmbeddingProfile profile)
    {
        if (!LocalProviders.Contains(profile.Provider, StringComparer.OrdinalIgnoreCase))
            throw new PrivacyPolicyException("MODEL_PROVIDER_NOT_LOCAL",
                $"Provider '{profile.Provider}' is not a local inference provider; hosted providers need a separate authorized change to AGENTS.md.");

        var approval = approvals.FirstOrDefault(a => a.Model == profile.Model && a.Revision == profile.Revision);
        if (approval is null || string.IsNullOrWhiteSpace(approval.License) || string.IsNullOrWhiteSpace(approval.ReviewedOn))
            throw new PrivacyPolicyException("MODEL_NOT_APPROVED",
                $"Model '{profile.Model}' at revision {profile.Revision} has no reviewed license/revision record, so its profile cannot be enabled.");
        if (approval.UsesRemoteCode && !approval.RemoteCodeReviewed)
            throw new PrivacyPolicyException("MODEL_REMOTE_CODE_UNREVIEWED",
                $"Model '{profile.Model}' uses remote code that has not been reviewed.");

        var path = ArtifactPath(artifactRoot, profile.Model, profile.Revision);
        if (!IsComplete(path, approval.Manifest ?? DefaultManifest(profile.Provider)))
            throw new PrivacyPolicyException("MODEL_ARTIFACT_MISSING",
                $"Pinned artifacts of '{profile.Model}' at revision {profile.Revision} are not in the model directory. "
                + "Run the separate model acquisition step described in docs/privacy.md; runs never download models.");
    }

    /// <summary>Each group has a file that resolves (through cache links) to a non-empty regular file under the artifact root.</summary>
    private bool IsComplete(string snapshot, IReadOnlyList<IReadOnlyList<string>> manifest) =>
        Directory.Exists(snapshot) && manifest.All(group => group.Any(file => ResolvesToFile(Path.Combine(snapshot, file))));

    private bool ResolvesToFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var target = info.ResolveLinkTarget(returnFinalTarget: true) ?? info;
            var root = Path.GetFullPath(artifactRoot) + Path.DirectorySeparatorChar;
            return target is FileInfo { Exists: true, Length: > 0 } && Path.GetFullPath(target.FullName).StartsWith(root, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false; // a link loop or an unreadable entry is not a usable artifact
        }
    }
}

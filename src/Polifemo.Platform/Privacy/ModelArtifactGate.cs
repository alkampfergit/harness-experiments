using Polifemo.Contracts.Errors;
using Polifemo.Contracts.Profiles;

namespace Polifemo.Platform.Privacy;

/// <summary>
/// The review record that must exist before a model profile is enabled: license, revision and whether the
/// model needs remote code, which runs arbitrary code from the model repository.
/// </summary>
public sealed record ModelApproval(
    string Model, string Revision, string License, bool UsesRemoteCode, bool RemoteCodeReviewed, string ReviewedOn);

/// <summary>
/// Model acquisition is a separate, explicit step (docs/privacy.md). At run time this gate only checks that the
/// reviewed, pinned artifacts are already on disk; it never downloads anything.
/// </summary>
public sealed class ModelArtifactGate(string artifactRoot, IReadOnlyList<ModelApproval> approvals)
{
    private static readonly string[] LocalProviders = ["sentence-transformers", "onnxruntime"];

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
        if (!Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any())
            throw new PrivacyPolicyException("MODEL_ARTIFACT_MISSING",
                $"Pinned artifacts of '{profile.Model}' at revision {profile.Revision} are not in the model directory. "
                + "Run the separate model acquisition step described in docs/privacy.md; runs never download models.");
    }
}

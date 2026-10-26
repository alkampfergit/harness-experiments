using System.Text.Json;
using System.Text.RegularExpressions;
using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Privacy;

/// <summary>The recorded review that must exist before a model profile is enabled.</summary>
public sealed record ModelReview(
    string Model,
    string Revision,
    string License,
    bool RequiresRemoteCode,
    string? RemoteCodeRevision,
    string ReviewedBy,
    string ReviewedOn);

/// <summary>
/// Checks, without any network access, that a pinned model was acquired in a separate step and reviewed.
/// Acquisition is never triggered from here: a missing artifact is an error that says how to fix it.
/// </summary>
public static partial class ModelArtifacts
{
    [GeneratedRegex("^[0-9a-fA-F]{40}$")]
    private static partial Regex CommitSha();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string DirectoryFor(string modelDirectory, string model, string revision) =>
        Path.Combine(modelDirectory, model.Replace('/', '_'), revision);

    public static IReadOnlyList<ModelReview> LoadReviews(string path)
    {
        if (!File.Exists(path))
            throw new ModelArtifactException("MODEL_REVIEW_MISSING", $"Review file '{path}' does not exist; no model profile can be enabled without a reviewed entry.");
        try
        {
            return JsonSerializer.Deserialize<List<ModelReview>>(File.ReadAllText(path), Json) ?? [];
        }
        catch (JsonException ex)
        {
            throw new ModelArtifactException("MODEL_REVIEW_INVALID", $"Review file '{path}' is not valid: {ex.Message}");
        }
    }

    /// <summary>Returns the artifact directory when the model is reviewed and present, otherwise throws.</summary>
    public static string Require(string model, string revision, string modelDirectory, IEnumerable<ModelReview> reviews)
    {
        if (!CommitSha().IsMatch(revision))
            throw new ModelArtifactException("MODEL_REVISION_NOT_PINNED", $"Revision '{revision}' of '{model}' is not an immutable 40-character commit SHA.");

        var review = reviews.FirstOrDefault(r => r.Model == model && string.Equals(r.Revision, revision, StringComparison.OrdinalIgnoreCase))
            ?? throw new ModelArtifactException("MODEL_NOT_REVIEWED",
                $"'{model}' at {revision} has no review record. Review its license, revision and any remote code, then add it to the review file.");
        if (string.IsNullOrWhiteSpace(review.License) || string.IsNullOrWhiteSpace(review.ReviewedBy) || string.IsNullOrWhiteSpace(review.ReviewedOn))
            throw new ModelArtifactException("MODEL_REVIEW_INCOMPLETE", $"The review of '{model}' must record license, reviewer and date.");
        if (review.RequiresRemoteCode && !CommitSha().IsMatch(review.RemoteCodeRevision ?? ""))
            throw new ModelArtifactException("MODEL_REMOTE_CODE_UNREVIEWED",
                $"'{model}' needs remote model code. Pin and review that code at a commit SHA (remoteCodeRevision) before enabling the profile.");

        var dir = DirectoryFor(modelDirectory, model, revision);
        if (!Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any())
            throw new ModelArtifactException("MODEL_ARTIFACT_MISSING",
                $"Model '{model}' at {revision} is not present in '{dir}'. Models are never downloaded at run time: "
                + "acquire the pinned revision in the separate, explicit acquisition step (docs/local-only.md), then run again.");
        return dir;
    }
}

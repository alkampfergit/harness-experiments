using System.Globalization;
using System.Text.RegularExpressions;
using Polifemo.Contracts.Errors;
using Polifemo.Contracts.Hashing;

namespace Polifemo.Contracts.Profiles;

/// <summary>
/// The versions that evolve independently. Which hash depends on which one: chunker and mapping versions feed
/// the chunk hash, the embedding profile and the input template feed the embedding-input hash, and the
/// extractor version feeds none of them (it acts through the data it produces).
/// </summary>
public sealed record ProfileVersions(
    string ExtractorVersion,
    string ChunkerVersion,
    string MappingVersion,
    string EmbeddingProfileId,
    string InputTemplateVersion);

/// <summary>Everything that decides which vector space an embedding lives in.</summary>
public sealed partial record EmbeddingProfile
{
    [GeneratedRegex("^[0-9a-fA-F]{40}$")]
    private static partial Regex CommitSha();

    public string Provider { get; }
    public string Model { get; }
    public string Revision { get; }
    public string DocumentPrefix { get; }
    public string QueryPrefix { get; }
    public string Tokenizer { get; }
    public int MaxTokens { get; }
    public int Dimension { get; }
    public string Pooling { get; }
    public bool Normalize { get; }

    public EmbeddingProfile(
        string provider, string model, string revision, string documentPrefix, string queryPrefix,
        string tokenizer, int maxTokens, int dimension, string pooling, bool normalize)
    {
        if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("Provider is required.", nameof(provider));
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Model is required.", nameof(model));
        if (!CommitSha().IsMatch(revision))
            throw new ArgumentException($"Revision '{revision}' is not an immutable commit SHA (a branch or tag such as 'main' can move).", nameof(revision));
        if (maxTokens <= 0) throw new ArgumentOutOfRangeException(nameof(maxTokens), "MaxTokens must be positive.");
        if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension), "Dimension must be positive.");

        Provider = provider;
        Model = model;
        Revision = revision;
        DocumentPrefix = documentPrefix;
        QueryPrefix = queryPrefix;
        Tokenizer = tokenizer;
        MaxTokens = maxTokens;
        Dimension = dimension;
        Pooling = pooling;
        Normalize = normalize;
    }

    /// <summary>The identity of the vector space: a hash of the canonical fields.</summary>
    public string ProfileId => Hashing.Hashing.Hash(
        "ep/v1", Provider, Model, Revision, DocumentPrefix, QueryPrefix, Tokenizer,
        MaxTokens.ToString(CultureInfo.InvariantCulture), Dimension.ToString(CultureInfo.InvariantCulture),
        Pooling, Normalize ? "true" : "false");

    /// <summary>Vector search must call this before querying: a query and an index must share one profile.</summary>
    public static void EnsureSameSpace(EmbeddingProfile query, EmbeddingProfile index)
    {
        if (query.ProfileId != index.ProfileId)
            throw new EmbeddingProfileMismatchException(index.ProfileId, query.ProfileId);
    }
}

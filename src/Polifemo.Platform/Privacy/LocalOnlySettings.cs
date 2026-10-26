using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Privacy;

/// <summary>
/// Server-side configuration, read from the environment only (never from a tracked file). Every endpoint is
/// validated at load time. Variables that try to enable hosted providers are rejected, not ignored.
/// </summary>
public sealed record LocalOnlySettings(
    LocalOnlyPolicy Policy,
    Uri Store,
    Uri Embedding,
    Uri Reranking,
    string ModelDirectory)
{
    public const string EmbeddingUrlVariable = "POLIFEMO_EMBEDDING_URL";
    public const string RerankingUrlVariable = "POLIFEMO_RERANKING_URL";
    public const string StoreUrlVariable = "ELASTICSEARCH_URL";
    public const string ModelDirectoryVariable = "POLIFEMO_MODEL_DIR";
    public const string ServiceHostsVariable = "POLIFEMO_ALLOWED_SERVICE_HOSTS";

    private static readonly string[] HostedFlags =
        ["POLIFEMO_ALLOW_HOSTED_EMBEDDINGS", "POLIFEMO_ALLOW_HOSTED_RERANKING", "POLIFEMO_ALLOW_EXTERNAL_QUERIES"];

    private const string DefaultStore = "http://localhost:9200";
    private const string DefaultEmbedding = "http://127.0.0.1:8081";
    private const string DefaultReranking = "http://127.0.0.1:8082";

    public static LocalOnlySettings FromEnvironment(Func<string, string?>? get = null)
    {
        get ??= Environment.GetEnvironmentVariable;
        foreach (var flag in HostedFlags)
            if (!string.IsNullOrWhiteSpace(get(flag)))
                throw new LocalOnlyPolicyException("LOCAL_ONLY_HOSTED_DENIED",
                    $"{flag} is set, but hosted embeddings, hosted reranking and external query transmission are denied. "
                    + "A configuration flag cannot authorize them; unset it.");

        var policy = LocalOnlyPolicy.FromEnvironment(get);
        return new LocalOnlySettings(
            policy,
            policy.Validate(EndpointKind.Store, Value(get, StoreUrlVariable, DefaultStore), StoreUrlVariable),
            policy.Validate(EndpointKind.Embedding, Value(get, EmbeddingUrlVariable, DefaultEmbedding), EmbeddingUrlVariable),
            policy.Validate(EndpointKind.Reranking, Value(get, RerankingUrlVariable, DefaultReranking), RerankingUrlVariable),
            Value(get, ModelDirectoryVariable, Path.Combine(".index", "models")));
    }

    private static string Value(Func<string, string?> get, string name, string fallback) =>
        string.IsNullOrWhiteSpace(get(name)) ? fallback : get(name)!;

    public Uri For(EndpointKind kind) => kind switch
    {
        EndpointKind.Store => Store,
        EndpointKind.Embedding => Embedding,
        _ => Reranking,
    };
}

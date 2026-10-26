namespace Polifemo.Platform.Privacy;

/// <summary>Every place that holds sample-derived data or diagnostics. Adding a value without a policy fails a test.</summary>
public enum DataStoreKind
{
    WorkingCopy,
    TokenSaveGraph,
    SearchIndex,
    EmbeddingCache,
    ExporterCheckpoint,
    ModelArtifacts,
    ResultReports,
    DiagnosticLogs,
    Traces,
    CliOutput,
}

public sealed record DataStorePolicy(DataStoreKind Kind, string Location, bool Confidential, bool IsDiagnosticChannel, string Protection);

/// <summary>The explicit protection policy per data-bearing store and diagnostic channel (docs/privacy.md mirrors it).</summary>
public static class DataStoreCatalog
{
    public static IReadOnlyList<DataStorePolicy> All { get; } =
    [
        new(DataStoreKind.WorkingCopy, ".index/work/<name>/", true, false,
            "Copy of the pinned commit. Owner-only, git-ignored. Never inside samples/."),
        new(DataStoreKind.TokenSaveGraph, ".index/work/<name>/.tokensave/", true, false,
            "Source-derived graph. Owner-only, read-only for the adapter, uploads disabled."),
        new(DataStoreKind.SearchIndex, "Elasticsearch volume (local service)", true, false,
            "Bound to 127.0.0.1, Basic authentication on, no TLS in development. Production needs TLS and disk encryption."),
        new(DataStoreKind.EmbeddingCache, ".index/cache/", true, false,
            "Vectors are source-derived. Owner-only; keys start with the authorization boundary; no cross-boundary sharing."),
        new(DataStoreKind.ExporterCheckpoint, ".index/state/", true, false,
            "Manifests list paths and hashes. Owner-only; advanced only after a complete publication."),
        new(DataStoreKind.ModelArtifacts, ".index/models/", false, false,
            "Public weights at a pinned, reviewed revision. Written only by the explicit acquisition step."),
        new(DataStoreKind.ResultReports, "results/", false, false,
            "Metrics and provenance only: no source text, query text, credentials or private URLs."),
        new(DataStoreKind.DiagnosticLogs, "stderr and .index/logs/", true, true,
            "Structured DiagnosticEvent only: codes and identifiers. Values scrubbed of URLs and secrets. Owner-only files."),
        new(DataStoreKind.Traces, "local OpenTelemetry exporter, off by default", true, true,
            "Exporter endpoints must pass the local endpoint policy; spans carry identifiers, never payload."),
        new(DataStoreKind.CliOutput, "stdout and stderr of polifemo", true, true,
            "JSON results and scrubbed error messages; no credentials; no source text."),
    ];

    public static DataStorePolicy For(DataStoreKind kind) => All.Single(p => p.Kind == kind);
}

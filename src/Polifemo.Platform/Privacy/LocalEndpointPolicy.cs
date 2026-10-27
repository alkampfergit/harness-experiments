using System.Net;
using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Privacy;

/// <summary>What a destination is used for. Each one is validated on its own, so allowing one never allows another.</summary>
public enum ProcessingCapability
{
    Embedding,
    Reranking,
    QueryTransmission,
    Store,
    Telemetry,
}

/// <summary>
/// The server-side allowlist of destinations that may receive sample-derived data: loopback addresses and
/// single-label service names of the local Compose stack. There is no flag that enables a hosted destination
/// (constitution I). Host names are never resolved, so the check works offline and cannot be fooled by DNS.
/// </summary>
public sealed class LocalEndpointPolicy
{
    /// <summary><c>elasticsearch</c> is the Compose service of the devcontainer; everything else is loopback.</summary>
    public static readonly LocalEndpointPolicy Default = new(["localhost", "elasticsearch"]);

    private readonly HashSet<string> _approvedNames;

    public LocalEndpointPolicy(IEnumerable<string> approvedLocalNames)
    {
        _approvedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in approvedLocalNames)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('.') || IPAddress.TryParse(name, out _))
                throw new ArgumentException("An approved local name is a single-label service name (no dots, not an address).", nameof(approvedLocalNames));
            _approvedNames.Add(name);
        }
    }

    public bool IsLocal(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
            return IPAddress.TryParse(uri.IdnHost, out var address) && IPAddress.IsLoopback(address);
        return _approvedNames.Contains(uri.IdnHost) || uri.IdnHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the parsed URL or throws <c>POLICY_*</c>. The message names the capability and a fingerprint, never the URL.</summary>
    public Uri Validate(ProcessingCapability capability, string url, string source = "configuration")
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new PrivacyPolicyException("POLICY_ENDPOINT_INVALID", $"The {capability} endpoint from {source} is not an absolute URL.");
        return Validate(capability, uri, source);
    }

    public Uri Validate(ProcessingCapability capability, Uri uri, string source = "configuration")
    {
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new PrivacyPolicyException("POLICY_URL_HAS_CREDENTIALS",
                $"The {capability} endpoint from {source} embeds credentials; credentials never belong in a URL or in tracked configuration.");
        if (!IsLocal(uri))
            throw new PrivacyPolicyException("POLICY_EXTERNAL_ENDPOINT",
                $"The {capability} endpoint from {source} (fingerprint {Scrubber.Fingerprint(uri.GetLeftPart(UriPartial.Authority))}) "
                + "is not loopback or an approved local service; hosted processing is not authorized.");
        return uri;
    }
}

/// <summary>Configured destinations per capability. A null destination means that capability is disabled.</summary>
public sealed record ProcessingSettings(string? EmbeddingUrl = null, string? RerankingUrl = null, string? QueryUrl = null);

/// <summary>The validated result of <see cref="Resolve"/>.</summary>
public sealed record ProcessingPolicy(Uri? Embedding, Uri? Reranking, Uri? Query)
{
    /// <summary>
    /// Validates the profile defaults and every caller override independently. An override that points outside
    /// the local boundary is rejected, not ignored, so the caller learns that the attempt was refused.
    /// </summary>
    public static ProcessingPolicy Resolve(LocalEndpointPolicy policy, ProcessingSettings defaults, ProcessingSettings? overrides = null)
    {
        policy.ValidateAll(defaults, "profile default");
        if (overrides is not null) policy.ValidateAll(overrides, "caller override");
        return new ProcessingPolicy(
            Pick(overrides?.EmbeddingUrl, defaults.EmbeddingUrl),
            Pick(overrides?.RerankingUrl, defaults.RerankingUrl),
            Pick(overrides?.QueryUrl, defaults.QueryUrl));

        static Uri? Pick(string? over, string? def) => (over ?? def) is { } url ? new Uri(url) : null;
    }
}

internal static class LocalEndpointPolicyExtensions
{
    internal static void ValidateAll(this LocalEndpointPolicy policy, ProcessingSettings settings, string source)
    {
        if (settings.EmbeddingUrl is { } e) policy.Validate(ProcessingCapability.Embedding, e, source);
        if (settings.RerankingUrl is { } r) policy.Validate(ProcessingCapability.Reranking, r, source);
        if (settings.QueryUrl is { } q) policy.Validate(ProcessingCapability.QueryTransmission, q, source);
    }
}

using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Privacy;

/// <summary>The services sample content may be sent to. Each kind is validated and denied independently.</summary>
public enum EndpointKind
{
    Store,
    Embedding,
    Reranking,
}

/// <summary>
/// The server-side rule that decides where sample content may go: loopback, or a single-label service name the
/// operator allowlisted (a Compose service such as <c>elasticsearch</c>). Hosted providers are not a setting:
/// no flag, environment variable or caller argument authorizes them (AGENTS.md, constitution I).
/// Diagnostics name the kind, the source and the host only, never the path, query or credentials of a URL.
/// </summary>
public sealed class LocalOnlyPolicy
{
    public static readonly IReadOnlyList<string> DefaultServiceHosts = ["elasticsearch"];

    public IReadOnlyList<string> ServiceHosts { get; }

    public LocalOnlyPolicy(IEnumerable<string>? serviceHosts = null)
    {
        var hosts = (serviceHosts ?? DefaultServiceHosts).Select(h => h.Trim().ToLowerInvariant()).Where(h => h.Length > 0).Distinct().ToList();
        foreach (var host in hosts)
        {
            if (host.Contains('.') || host.Contains(':') || Uri.CheckHostName(host) != UriHostNameType.Dns)
                throw new LocalOnlyPolicyException("LOCAL_ONLY_BAD_ALLOWLIST",
                    $"Allowed service host '{host}' must be a single-label name (such as a Compose service), not an address or a dotted domain.");
        }
        ServiceHosts = hosts;
    }

    public static LocalOnlyPolicy FromEnvironment(Func<string, string?>? get = null)
    {
        var hosts = (get ?? Environment.GetEnvironmentVariable)(LocalOnlySettings.ServiceHostsVariable);
        return new LocalOnlyPolicy(string.IsNullOrWhiteSpace(hosts) ? null : hosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>Returns the endpoint when it is local, otherwise throws with an explicit diagnostic.</summary>
    public Uri Validate(EndpointKind kind, string url, string source)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new LocalOnlyPolicyException("LOCAL_ONLY_INVALID_ENDPOINT",
                $"{kind} endpoint from {source} is not an absolute http(s) URL.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new LocalOnlyPolicyException("LOCAL_ONLY_CREDENTIALS_IN_URL",
                $"{kind} endpoint from {source} contains credentials; pass them through the environment, never in a URL.");
        var host = uri.IdnHost.ToLowerInvariant();
        if (!uri.IsLoopback && !ServiceHosts.Contains(host))
            throw new LocalOnlyPolicyException("LOCAL_ONLY_EXTERNAL_ENDPOINT",
                $"{kind} endpoint from {source} points at '{host}', which is not local. Allowed: loopback and "
                + $"[{string.Join(", ", ServiceHosts)}]. Hosted providers are denied; configuration cannot authorize them.");
        return uri;
    }

    /// <summary>
    /// Picks the endpoint for a request. A caller override is validated exactly like the configured value, so it
    /// can redirect to another local service but never to an external one.
    /// </summary>
    public Uri Resolve(EndpointKind kind, string configured, string? callerOverride)
    {
        var effective = Validate(kind, configured, "server configuration");
        return callerOverride is null ? effective : Validate(kind, callerOverride, "caller override");
    }
}

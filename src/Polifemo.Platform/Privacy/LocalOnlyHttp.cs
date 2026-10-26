using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Privacy;

/// <summary>
/// Refuses a request whose destination fails <see cref="LocalEndpointPolicy"/> before a single byte is sent.
/// This is where "no payload-bearing egress" is enforced for indexing, search and reranking traffic: every
/// <see cref="HttpClient"/> that carries sample-derived data is built through <see cref="LocalOnlyHttp"/>.
/// </summary>
public sealed class LocalOnlyHandler(LocalEndpointPolicy policy, ProcessingCapability capability, HttpMessageHandler inner)
    : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new PrivacyPolicyException("POLICY_ENDPOINT_INVALID", $"A {capability} request has no absolute URL.");
        policy.Validate(capability, uri, "request");
        return base.SendAsync(request, cancellationToken);
    }
}

public static class LocalOnlyHttp
{
    /// <summary>
    /// Redirects are not followed (a local service could redirect outward) and system proxies are not used
    /// (an environment proxy would carry the payload off the machine).
    /// </summary>
    public static HttpClient CreateClient(LocalEndpointPolicy policy, ProcessingCapability capability, HttpMessageHandler? inner = null)
    {
        inner ??= new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        return new HttpClient(new LocalOnlyHandler(policy, capability, inner));
    }
}

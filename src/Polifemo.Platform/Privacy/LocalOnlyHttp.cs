using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Privacy;

/// <summary>
/// The only way product code creates an <see cref="HttpClient"/> (a test enforces it). Every request is checked
/// against the policy before it reaches the network, the system proxy is ignored and redirects are not followed,
/// so a payload cannot be forwarded to another host.
/// </summary>
public static class LocalOnlyHttp
{
    public static HttpClient CreateClient(LocalOnlyPolicy policy, EndpointKind kind, HttpMessageHandler? inner = null, TimeSpan? timeout = null) =>
        new(new GuardHandler(policy, kind, inner ?? CreateTransport())) { Timeout = timeout ?? TimeSpan.FromSeconds(30) };

    internal static SocketsHttpHandler CreateTransport() => new() { UseProxy = false, AllowAutoRedirect = false };

    private sealed class GuardHandler(LocalOnlyPolicy policy, EndpointKind kind, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            policy.Validate(kind, request.RequestUri?.OriginalString ?? "", "request");
            return base.SendAsync(request, cancellationToken);
        }
    }
}

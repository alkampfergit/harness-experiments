using System.Net;
using System.Security.Cryptography;
using System.Text;
using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Privacy;

/// <summary>
/// Rules for a local inference service (the embedding and reranking sidecar): it binds to loopback only,
/// requires a bearer token that comes from the environment, and bounds every request.
/// </summary>
public sealed class InferenceServiceSecurity
{
    public const string TokenVariable = "POLIFEMO_INFERENCE_TOKEN";
    public const int MinTokenLength = 16;

    private readonly byte[] _tokenHash;

    public InferenceServiceSecurity(string token, RequestLimits? limits = null)
    {
        if (token.Length < MinTokenLength)
            throw new PrivacyPolicyException("AUTH_TOKEN_WEAK", $"The inference token must have at least {MinTokenLength} characters.");
        _tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        Limits = limits ?? new RequestLimits();
    }

    public RequestLimits Limits { get; }

    /// <summary>The token is read from the environment only; it is never part of tracked configuration.</summary>
    public static InferenceServiceSecurity FromEnvironment(Func<string, string?> getVariable, RequestLimits? limits = null) =>
        new(getVariable(TokenVariable) is { Length: > 0 } token
            ? token
            : throw new PrivacyPolicyException("AUTH_TOKEN_MISSING", $"Set {TokenVariable} before starting or calling the inference service."), limits);

    /// <summary>Only loopback addresses and <c>localhost</c> are valid bind addresses.</summary>
    public static IPAddress ValidateBindAddress(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return IPAddress.Loopback;
        if (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address)) return address;
        throw new PrivacyPolicyException("POLICY_BIND_NOT_LOCAL", "The inference service may only bind to a loopback address.");
    }

    /// <summary>Constant-time comparison of an <c>Authorization</c> header value with the configured token.</summary>
    public bool IsAuthorized(string? authorizationHeader)
    {
        const string prefix = "Bearer ";
        if (authorizationHeader is null || !authorizationHeader.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(authorizationHeader[prefix.Length..]));
        return CryptographicOperations.FixedTimeEquals(presented, _tokenHash);
    }
}

public sealed record RequestLimits(int MaxBodyBytes = 1_048_576, int MaxBatchInputs = 64)
{
    public void Check(long bodyBytes, int inputs)
    {
        if (bodyBytes > MaxBodyBytes)
            throw new PrivacyPolicyException("LIMIT_BODY_TOO_LARGE", $"Request body exceeds {MaxBodyBytes} bytes.");
        if (inputs > MaxBatchInputs)
            throw new PrivacyPolicyException("LIMIT_BATCH_TOO_LARGE", $"Request has more than {MaxBatchInputs} inputs.");
    }
}

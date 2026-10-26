using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Polifemo.Platform.Privacy;

/// <summary>A pattern that identifies a secret. The same rules drive input screening and diagnostic scrubbing.</summary>
public sealed record SecretRule(string Id, Regex Pattern);

public static partial class SecretRules
{
    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----(?:[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----|[\s\S]*)")]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b")]
    private static partial Regex AwsAccessKey();

    [GeneratedRegex(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\bbearer\s+[A-Za-z0-9._~+/=-]{16,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\b(?:password|passwd|pwd|secret|api[_-]?key|token)\b[""']?\s*[:=]\s*(?:""(?:[^""\\]|\\.){4,}""|'(?:[^'\\]|\\.){4,}')", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedAssignment();

    [GeneratedRegex(@"\bpassword=[^;\s""']{4,}", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPassword();

    public const string PrivateKey = "private-key-block";
    public const string AwsKey = "aws-access-key";
    public const string GitHub = "github-token";
    public const string Bearer = "bearer-token";
    public const string Assignment = "secret-assignment";
    public const string ConnectionString = "connection-string-password";

    public static IReadOnlyList<SecretRule> All { get; } =
    [
        new(PrivateKey, PrivateKeyBlock()),
        new(AwsKey, AwsAccessKey()),
        new(GitHub, GitHubToken()),
        new(Bearer, BearerToken()),
        new(Assignment, QuotedAssignment()),
        new(ConnectionString, ConnectionStringPassword()),
    ];
}

/// <summary>Removes URLs and secrets from text that is about to be shown or logged.</summary>
public static partial class Scrubber
{
    [GeneratedRegex(@"[a-zA-Z][a-zA-Z0-9+.-]*://[^\s""'<>]+")]
    private static partial Regex Url();

    public static string Fingerprint(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];

    public static string Scrub(string text)
    {
        var result = Url().Replace(text, m => $"[url:{Fingerprint(m.Value)}]");
        foreach (var rule in SecretRules.All)
            result = rule.Pattern.Replace(result, $"[redacted:{rule.Id}]");
        return result;
    }
}

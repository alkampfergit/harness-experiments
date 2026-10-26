using System.Text;
using System.Text.RegularExpressions;
using Polifemo.Contracts.Errors;
using Polifemo.Contracts.Identity;

namespace Polifemo.Platform.Privacy;

/// <summary>What happens to an input before it is indexed or sent to inference.</summary>
public enum InputAction
{
    Include,
    Redact,
    Exclude,
    Reject,
}

/// <summary>
/// The outcome for one file. A redacted file carries its new <see cref="Content"/>; the redaction is always
/// listed in <see cref="RuleIds"/>, so it is never a silent change. <see cref="ContentHash"/> is the hash of the
/// bytes that will be indexed, which for a redacted file differs from the hash of the original.
/// </summary>
public sealed record InputDecision(string Path, InputAction Action, IReadOnlyList<string> RuleIds, string? Content = null, FileHash? ContentHash = null)
{
    /// <summary>Fails the run for a rejected input. The message names the file and rules, never the matched text.</summary>
    public InputDecision ThrowIfRejected() => Action == InputAction.Reject
        ? throw new PrivacyPolicyException("INPUT_REJECTED", $"'{Path}' was rejected by input rule(s): {string.Join(", ", RuleIds)}.")
        : this;
}

public sealed record PathRule(string Id, Regex Pattern, InputAction Action);

/// <summary>
/// Source and exclusion rules applied before indexing or inference. Path rules drop files that normally hold
/// secrets; content rules look for secret material inside the rest. Each rule has one explicit action, and the
/// most severe action among the matches wins (Reject, then Exclude, then Redact).
/// </summary>
public sealed partial class SourceInputPolicy
{
    [GeneratedRegex(@"(^|/)\.env(\..*)?$")]
    private static partial Regex EnvFile();

    [GeneratedRegex(@"\.(pem|key|pfx|p12|jks|keystore)$|(^|/)id_(rsa|dsa|ecdsa|ed25519)(\.pub)?$", RegexOptions.IgnoreCase)]
    private static partial Regex KeyMaterial();

    [GeneratedRegex(@"(^|/)(\.npmrc|\.netrc|credentials|secrets\.json)$", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialFile();

    [GeneratedRegex(@"(^|/)\.(git|tokensave)/")]
    private static partial Regex ToolInternals();

    private readonly IReadOnlyList<PathRule> _pathRules;
    private readonly IReadOnlyDictionary<string, InputAction> _secretActions;

    public SourceInputPolicy(IReadOnlyList<PathRule> pathRules, IReadOnlyDictionary<string, InputAction> secretActions)
    {
        _pathRules = pathRules;
        _secretActions = secretActions;
    }

    /// <summary>Key material and tokens exclude the whole file; looser matches are redacted and reported.</summary>
    public static SourceInputPolicy Default { get; } = new(
        [
            new("env-file", EnvFile(), InputAction.Exclude),
            new("key-material", KeyMaterial(), InputAction.Exclude),
            new("credential-file", CredentialFile(), InputAction.Exclude),
            new("tool-internals", ToolInternals(), InputAction.Exclude),
        ],
        new Dictionary<string, InputAction>
        {
            [SecretRules.PrivateKey] = InputAction.Exclude,
            [SecretRules.AwsKey] = InputAction.Exclude,
            [SecretRules.GitHub] = InputAction.Exclude,
            [SecretRules.Bearer] = InputAction.Redact,
            [SecretRules.Assignment] = InputAction.Redact,
            [SecretRules.ConnectionString] = InputAction.Redact,
        });

    public InputDecision Evaluate(string path, ReadOnlySpan<byte> bytes)
    {
        var normalized = Polifemo.Contracts.Hashing.Normalize.Path(path);
        var pathHits = _pathRules.Where(r => r.Pattern.IsMatch(normalized)).ToList();
        if (pathHits.Count > 0)
            return Decide(normalized, pathHits.Select(r => (r.Id, r.Action)).ToList(), null);

        if (bytes.Contains((byte)0)) return new InputDecision(normalized, InputAction.Exclude, ["binary-file"]);

        var text = Encoding.UTF8.GetString(bytes);
        var hits = SecretRules.All
            .Where(r => _secretActions.TryGetValue(r.Id, out var action) && action != InputAction.Include && r.Pattern.IsMatch(text))
            .Select(r => (r.Id, _secretActions[r.Id]))
            .ToList();
        return hits.Count == 0 ? new InputDecision(normalized, InputAction.Include, []) : Decide(normalized, hits, text);
    }

    private static InputDecision Decide(string path, List<(string Id, InputAction Action)> hits, string? text)
    {
        var action = hits.Max(h => h.Action);
        var ids = hits.Where(h => h.Action == action).Select(h => h.Id).Distinct().ToList();
        if (action != InputAction.Redact || text is null) return new InputDecision(path, action, ids);

        foreach (var (id, _) in hits)
        {
            var rule = SecretRules.All.First(r => r.Id == id);
            text = rule.Pattern.Replace(text, $"[REDACTED:{id}]");
        }
        return new InputDecision(path, action, ids, text, FileHash.Create(Encoding.UTF8.GetBytes(text)));
    }
}

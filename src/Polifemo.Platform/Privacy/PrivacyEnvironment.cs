namespace Polifemo.Platform.Privacy;

/// <summary>
/// Environment every child process gets: offline model hubs and no vendor telemetry. These settings reduce
/// accidental egress; they are not a network control. The enforcement of offline operation beyond this is a
/// network policy of the host (docs/privacy.md).
/// </summary>
public static class PrivacyEnvironment
{
    public static readonly IReadOnlyDictionary<string, string> ForChildProcess = new Dictionary<string, string>
    {
        ["HF_HUB_OFFLINE"] = "1",
        ["TRANSFORMERS_OFFLINE"] = "1",
        ["HF_HUB_DISABLE_TELEMETRY"] = "1",
        ["DO_NOT_TRACK"] = "1",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["OTEL_SDK_DISABLED"] = "true",
    };

    /// <summary>The caller's variables first, then the privacy variables on top: a caller cannot switch them off.</summary>
    public static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string>? callerEnvironment)
    {
        var merged = new Dictionary<string, string>(callerEnvironment ?? new Dictionary<string, string>());
        foreach (var (key, value) in ForChildProcess) merged[key] = value;
        return merged;
    }
}

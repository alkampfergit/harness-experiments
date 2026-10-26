namespace Polifemo.Platform.Privacy;

/// <summary>Environment for every model-serving child process: offline, no telemetry, no hub access.</summary>
public static class OfflineEnvironment
{
    public static readonly IReadOnlyDictionary<string, string> Variables = new Dictionary<string, string>
    {
        ["HF_HUB_OFFLINE"] = "1",
        ["TRANSFORMERS_OFFLINE"] = "1",
        ["HF_DATASETS_OFFLINE"] = "1",
        ["HF_HUB_DISABLE_TELEMETRY"] = "1",
        ["DO_NOT_TRACK"] = "1",
    };

    /// <summary>Names of the offline variables that the given environment does not set to their required value.</summary>
    public static IReadOnlyList<string> Missing(Func<string, string?>? get = null)
    {
        get ??= Environment.GetEnvironmentVariable;
        return Variables.Where(v => get(v.Key) != v.Value).Select(v => v.Key).ToList();
    }
}

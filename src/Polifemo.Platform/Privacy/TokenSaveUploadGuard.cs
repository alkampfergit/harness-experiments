using System.Text.Json;

namespace Polifemo.Platform.Privacy;

public enum UploadSetting
{
    Disabled,
    Enabled,

    /// <summary>The configuration does not state the setting, so the tool default (unknown to us) would apply.</summary>
    Unspecified,
}

/// <summary>
/// Reads the TokenSave project setting that turns off optional usage reporting. This is a setting, not a network
/// control: the egress restrictions that actually prove offline operation are documented in docs/privacy.md.
/// </summary>
public static class TokenSaveUploadGuard
{
    public static UploadSetting Read(string worktree)
    {
        var config = Path.Combine(worktree, ".tokensave", "config.json");
        if (!File.Exists(config)) return UploadSetting.Unspecified;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(config));
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("report_savings", out var value))
                return UploadSetting.Unspecified;
            return value.ValueKind switch
            {
                JsonValueKind.False => UploadSetting.Disabled,
                JsonValueKind.True => UploadSetting.Enabled,
                _ => UploadSetting.Unspecified,
            };
        }
        catch (JsonException)
        {
            return UploadSetting.Unspecified;
        }
    }
}

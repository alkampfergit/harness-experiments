using System.Text.Json;
using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Privacy;

/// <summary>
/// Checks the optional TokenSave upload setting of a worktree. This is one layer only: it does not stop every
/// possible request, so offline runs also need network controls (docs/local-only.md).
/// </summary>
public static class TokenSaveUploads
{
    public static void EnsureDisabled(string worktree)
    {
        var config = Path.Combine(worktree, ".tokensave", "config.json");
        if (!File.Exists(config))
            throw new LocalOnlyPolicyException("LOCAL_ONLY_TOKENSAVE_CONFIG_MISSING",
                $"'{config}' does not exist, so upload settings cannot be verified; run tokensave init and set report_savings to false.");
        using var doc = JsonDocument.Parse(File.ReadAllText(config));
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("report_savings", out var flag)
            || flag.ValueKind != JsonValueKind.False)
            throw new LocalOnlyPolicyException("LOCAL_ONLY_TOKENSAVE_UPLOAD",
                $"report_savings in '{config}' is not false; TokenSave uploads must be disabled (also run 'tokensave disable-upload-counter').");
    }
}

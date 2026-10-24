using System.Diagnostics;

namespace Polifemo.Tests.Support;

/// <summary>Fails (never skips) when the pinned TokenSave build is not on PATH.</summary>
public static class RequireTokenSave
{
    public const string PinnedVersion = "7.16.0";

    public static void Ensure()
    {
        string output;
        try
        {
            var psi = new ProcessStartInfo("tokensave", "--version") { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"tokensave {PinnedVersion} is required on PATH but could not be started.", ex);
        }
        if (!output.EndsWith(PinnedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"tokensave {PinnedVersion} is required, found '{output}'.");
    }
}

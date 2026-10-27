using System.Diagnostics;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.Privacy;

namespace Polifemo.Platform.Processes;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Runs external tools (git, tokensave). Never logs the environment, and always sets the offline/no-telemetry variables of <see cref="PrivacyEnvironment"/>. The child gets no stdin (it sees
/// end-of-file at once), so a tool that prompts when it finds a terminal cannot block a run, and every
/// call has a timeout.
/// </summary>
public static class ProcessRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> args,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;
        // Offline and no-telemetry variables always win over the caller's (see PrivacyEnvironment).
        foreach (var (k, v) in PrivacyEnvironment.Merge(environment)) psi.Environment[k] = v;

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Cannot start '{fileName}'.");
        p.StandardInput.Close();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout ?? DefaultTimeout);
        var stdout = p.StandardOutput.ReadToEndAsync(limit.Token);
        var stderr = p.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await p.WaitForExitAsync(limit.Token);
            return new ProcessResult(p.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException)
            {
                // The process already exited, so there is nothing left to stop.
            }
            if (ct.IsCancellationRequested) throw;
            throw new PolifemoException("PROCESS_TIMEOUT", $"'{fileName}' did not finish within {(timeout ?? DefaultTimeout).TotalSeconds:0} s and was stopped.");
        }
    }
}

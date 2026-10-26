using System.Text.Json;
using System.Text.Json.Serialization;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.Privacy;

namespace Polifemo.Cli;

/// <summary>JSON on stdout, diagnostics on stderr. Never prints credentials, private URLs or sample source: error text goes through <see cref="Scrubber"/>.</summary>
public static class CliOutput
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void WriteResult(TextWriter stdout, object result) =>
        stdout.WriteLine(JsonSerializer.Serialize(result, Json));

    public static void WriteError(TextWriter stdout, TextWriter stderr, Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            stderr.WriteLine("Cancelled.");
            return;
        }
        // Typed messages are written without payload and are scrubbed again; other messages can echo input and are dropped.
        var diagnostic = DiagnosticEvent.FromException("cli", ex);
        var message = ex is PolifemoException ? Scrubber.Scrub(ex.Message) : $"Unexpected {ex.GetType().Name}.";
        stderr.WriteLine($"{diagnostic.Code}: {message}");
        WriteResult(stdout, new { ok = false, error = new { code = diagnostic.Code, message } });
    }

    /// <summary>Runs a command body, converting exceptions to an exit code and never a partial success.</summary>
    public static async Task<int> RunAsync(TextWriter stdout, TextWriter stderr, Func<Task<(int Exit, object Result)>> body)
    {
        try
        {
            var (exit, result) = await body();
            WriteResult(stdout, result);
            return exit;
        }
        catch (Exception ex)
        {
            WriteError(stdout, stderr, ex);
            return ExitCodes.For(ex);
        }
    }
}

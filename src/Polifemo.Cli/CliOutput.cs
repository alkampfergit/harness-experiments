using System.Text.Json;
using System.Text.Json.Serialization;
using Polifemo.Contracts.Errors;

namespace Polifemo.Cli;

/// <summary>JSON on stdout, diagnostics on stderr. Never prints credentials or sample source.</summary>
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
        var code = ex is PolifemoException p ? p.Code : "UNEXPECTED";
        stderr.WriteLine($"{code}: {ex.Message}");
        WriteResult(stdout, new { ok = false, error = new { code, message = ex.Message } });
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

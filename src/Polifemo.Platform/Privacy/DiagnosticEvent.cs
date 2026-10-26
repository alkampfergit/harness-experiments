using System.Text.Json;
using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Privacy;

/// <summary>
/// A structured, privacy-safe diagnostic. It holds a stable code, the component, and identifier-like fields
/// (ids, counts, stage names). Fields that would carry payload are refused by name, and every value is
/// scrubbed of URLs and secrets, so logs and traces keep their usefulness without raw code, queries,
/// credentials or private URLs.
/// </summary>
public sealed class DiagnosticEvent
{
    private const int MaxValueLength = 200;

    private static readonly string[] ForbiddenFieldParts =
        ["text", "query", "source", "content", "body", "snippet", "url", "uri", "password", "secret", "token", "authorization", "credential", "header"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Code { get; }
    public string Component { get; }
    public IReadOnlyDictionary<string, string> Fields { get; }

    private DiagnosticEvent(string code, string component, IReadOnlyDictionary<string, string> fields)
    {
        Code = code;
        Component = component;
        Fields = fields;
    }

    public static DiagnosticEvent Create(string code, string component, IReadOnlyDictionary<string, string>? fields = null)
    {
        var clean = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in fields ?? new Dictionary<string, string>())
        {
            if (ForbiddenFieldParts.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Diagnostic field '{name}' may carry payload; use an identifier field instead.", nameof(fields));
            clean[name] = Clean(value);
        }
        return new DiagnosticEvent(code, component, clean);
    }

    /// <summary>
    /// Keeps the failure cause (code, exception types) and the identifiers the caller supplies. The message is
    /// kept only for typed Polifemo failures, whose messages are written without payload; the message of any
    /// other exception can echo input (a parser quoting the source it choked on) and is dropped.
    /// </summary>
    public static DiagnosticEvent FromException(string component, Exception ex, IReadOnlyDictionary<string, string>? identifiers = null)
    {
        var fields = new Dictionary<string, string>(identifiers ?? new Dictionary<string, string>())
        {
            ["exception_type"] = ex.GetType().Name,
        };
        if (ex.InnerException is { } inner) fields["cause_type"] = inner.GetType().Name;
        if (ex is PolifemoException) fields["detail"] = ex.Message;
        return Create(ex is PolifemoException p ? p.Code : "UNEXPECTED", component, fields);
    }

    public string ToJson() => JsonSerializer.Serialize(new { code = Code, component = Component, fields = Fields }, Json);

    private static string Clean(string value)
    {
        var scrubbed = Scrubber.Scrub(value.ReplaceLineEndings(" "));
        return scrubbed.Length <= MaxValueLength ? scrubbed : scrubbed[..MaxValueLength] + "…";
    }
}

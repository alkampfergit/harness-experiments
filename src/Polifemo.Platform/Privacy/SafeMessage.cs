using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Privacy;

/// <summary>
/// The one policy for turning a failure into text that may be logged or printed. Codes, exception types and
/// identifiers are always kept. A <see cref="PolifemoException"/> message is kept (scrubbed) only when its code
/// is not in <see cref="OpaqueCodes"/>; those codes wrap subprocess output (stderr can quote source or query
/// text) and are replaced by a fixed text. Messages of any other exception are never copied.
/// </summary>
public static class SafeMessage
{
    private static readonly Dictionary<string, string> OpaqueCodes = new(StringComparer.Ordinal)
    {
        ["GIT_FAILED"] = "A git command failed; its output is withheld because it may contain source or query text.",
        ["PREP_INDEX_FAILED"] = "Indexing the prepared sample failed; the tool output is withheld because it may contain source text.",
    };

    /// <summary>The message to show for <paramref name="ex"/>, or null when none may be shown.</summary>
    public static string? For(Exception ex) => ex switch
    {
        PolifemoException p when OpaqueCodes.TryGetValue(p.Code, out var fixedText) => fixedText,
        PolifemoException p => Scrubber.Scrub(p.Message),
        _ => null,
    };
}

using System.Text;
using System.Text.RegularExpressions;

namespace Polifemo.Contracts.Hashing;

public static partial class Normalize
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Relative to the repository root, <c>/</c> separators, no leading <c>./</c>, case kept, NFC.
    /// A rooted path or one that climbs out of the root is an error, not something to repair.
    /// </summary>
    public static string Path(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path must not be empty.", nameof(path));
        var p = path.Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
        if (p.StartsWith('/') || (p.Length > 1 && p[1] == ':') || p.Split('/').Contains(".."))
            throw new ArgumentException($"'{path}' must be a path relative to the repository root.", nameof(path));
        return p.Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// The declaration part of a TokenSave signature: an expression body (<c>=&gt; ...</c>, observed on 7.16.0
    /// to be part of the signature) is cut off, because editing a body must not change an identity. Whitespace
    /// runs collapse to one space and the result is trimmed. A missing signature is the empty string.
    /// </summary>
    public static string Signature(string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) return "";
        var text = signature;
        var depth = 0;
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] is '(' or '[' or '{') depth++;
            else if (text[i] is ')' or ']' or '}') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && text[i] == '=' && text[i + 1] == '>')
            {
                text = text[..i];
                break;
            }
        }
        return Whitespace().Replace(text, " ").Trim();
    }
}

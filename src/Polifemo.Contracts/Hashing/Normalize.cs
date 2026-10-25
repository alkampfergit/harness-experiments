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
            if (SkipLiteralOrComment(text, ref i)) continue;
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

    /// <summary>
    /// When a string, character or interpolated literal (regular, verbatim, raw) or a comment starts at
    /// <paramref name="i"/>, moves <paramref name="i"/> to its last character and returns true.
    /// An unterminated literal swallows the rest of the text.
    /// </summary>
    private static bool SkipLiteralOrComment(string text, ref int i)
    {
        if (text[i] == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*')
        {
            i = text[i + 1] == '/' ? EndOfLineComment(text, i) : EndOfBlockComment(text, i);
            return true;
        }

        if (text[i] == '\'')
        {
            i = EndOfCharLiteral(text, i);
            return true;
        }

        return TrySkipString(text, ref i);
    }

    private static int EndOfLineComment(string text, int i)
    {
        var nl = text.IndexOf('\n', i);
        return nl < 0 ? text.Length - 1 : nl;
    }

    private static int EndOfBlockComment(string text, int i)
    {
        var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
        return end < 0 ? text.Length - 1 : end + 1;
    }

    private static int EndOfCharLiteral(string text, int i)
    {
        var j = i + 1;
        while (j < text.Length)
        {
            if (text[j] == '\\') j += 2;
            else if (text[j] == '\'') return j;
            else j++;
        }
        return text.Length - 1;
    }

    private static bool TrySkipString(string text, ref int i)
    {
        // Prefixes $ and @ (any order) before the opening quote; raw strings start with three or more quotes.
        var start = i;
        while (start < text.Length && text[start] is '$' or '@') start++;
        if (start >= text.Length || text[start] != '"') return false;

        var verbatim = text.AsSpan(i, start - i).Contains('@');
        var quotes = 0;
        while (start + quotes < text.Length && text[start + quotes] == '"') quotes++;
        i = quotes >= 3 ? EndOfRawString(text, start, quotes) : EndOfRegularString(text, start, verbatim);
        return true;
    }

    private static int EndOfRawString(string text, int start, int quotes)
    {
        var close = text.IndexOf(new string('"', quotes), start + quotes, StringComparison.Ordinal);
        return close < 0 ? text.Length - 1 : close + quotes - 1;
    }

    private static int EndOfRegularString(string text, int start, bool verbatim)
    {
        var j = start + 1;
        while (j < text.Length)
        {
            if (!verbatim && text[j] == '\\') j += 2;
            else if (text[j] != '"') j++;
            else if (verbatim && j + 1 < text.Length && text[j + 1] == '"') j += 2;
            else return j;
        }
        return text.Length - 1;
    }
}

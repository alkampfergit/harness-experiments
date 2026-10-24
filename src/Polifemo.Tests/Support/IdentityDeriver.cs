using System.Text;
using Polifemo.Contracts.Hashing;
using Polifemo.Contracts.Identity;
using Polifemo.Contracts.Provenance;

namespace Polifemo.Tests.Support;

public sealed record DerivedSymbol(
    string Path, string QualifiedName, string Kind, string Signature, string MatchId,
    LogicalKey Key, FileHash File, OccurrenceKey Occurrence, ChunkHash Chunk, EmbeddingInputHash Embedding,
    int StartLine, int EndLine)
{
    /// <summary>Every identity value and the location, for "nothing changed" assertions.</summary>
    public (LogicalKey, FileHash, OccurrenceKey, ChunkHash, EmbeddingInputHash, int, int) All =>
        (Key, File, Occurrence, Chunk, Embedding, StartLine, EndLine);
}

/// <summary>
/// Derives the contract identities for every symbol of a fixture state, from its expectation record (symbols,
/// extents, document associations) and the raw bytes of its files. TokenSave is not needed.
/// </summary>
public static class IdentityDeriver
{
    public const string RepositoryId = "synthetic-csharp";
    public const string ChunkerVersion = "chunker-test-1";
    public const string MappingVersion = "mapping-test-1";
    public const string ProfileId = "profile-test-1";
    public const string TemplateVersion = "template-test-1";

    public static readonly SourceScope DefaultScope = new("local", RepositoryId, "main", "wt1", "snap1");

    public static IReadOnlyList<DerivedSymbol> Derive(ExpectationRecord record, string worktree, SourceScope? scope = null)
    {
        scope ??= DefaultScope;
        var result = new List<DerivedSymbol>();
        var texts = new Dictionary<string, string>();

        foreach (var file in record.Symbols.GroupBy(s => s.Path).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(Path.Combine(worktree, file.Key));
            var fileHash = FileHash.Create(bytes);
            var lines = FileEncoding.Decode(bytes).Replace("\r\n", "\n").Split('\n');

            var symbols = file.OrderBy(s => s.StartLine).ThenBy(s => s.QualifiedName, StringComparer.Ordinal).ToList();
            var keys = LogicalKeys.ForFile(RepositoryId, file.Key,
                symbols.Select(s => new SymbolDescriptor(s.QualifiedName, s.Kind, s.Signature, s.StartLine)).ToList());

            var docLinks = record.DocAssociations
                .Where(d => d.Stale != true && d.AppliesTo.Contains(file.Key))
                .Select(d => d.Doc).Order(StringComparer.Ordinal).ToList();
            var docText = docLinks.Select(d => texts.TryGetValue(d, out var t) ? t : texts[d] = DocBody(Path.Combine(worktree, d))).ToList();

            var ordinals = new Dictionary<string, int>();
            for (var i = 0; i < symbols.Count; i++)
            {
                var s = symbols[i];
                var signature = Normalize.Signature(s.Signature);
                var text = string.Join("\n", lines[(s.StartLine - 1)..s.EndLine]);
                var key = keys[i];

                var baseId = $"{s.QualifiedName}|{s.Kind}|{signature}";
                var ordinal = ordinals.GetValueOrDefault(baseId);
                ordinals[baseId] = ordinal + 1;

                var chunk = ChunkHash.Create(ChunkerVersion, MappingVersion, key,
                    new { kind = s.Kind, qualifiedName = s.QualifiedName, signature, text, docLinks, docText });
                var embedding = EmbeddingInputHash.Create(ProfileId, TemplateVersion, "document",
                    Encoding.UTF8.GetBytes($"{s.Kind} {s.QualifiedName}\n{signature}\n{text}"));

                result.Add(new DerivedSymbol(s.Path, s.QualifiedName, s.Kind, signature, $"{baseId}|{ordinal}",
                    key, fileHash, OccurrenceKey.Create(key, scope), chunk, embedding, s.StartLine, s.EndLine));
            }
        }
        return result;
    }

    /// <summary>A document's text without its front matter: the associations are a separate chunk field.</summary>
    private static string DocBody(string path)
    {
        var text = FileEncoding.Decode(File.ReadAllBytes(path)).Replace("\r\n", "\n");
        if (!text.StartsWith("---\n", StringComparison.Ordinal)) return text;
        var end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        return end < 0 ? text : text[(end + 5)..];
    }
}

using System.Text;
using System.Text.RegularExpressions;
using Polifemo.Platform.TokenSave;

namespace Polifemo.Tests.Support;

/// <summary>
/// Turns what TokenSave extracted into an <see cref="ExpectationRecord"/>. The same function writes the
/// recorded files (<c>UPDATE_EXPECTATIONS=1</c>) and produces the observation the tests compare them with,
/// so the policy for what counts as unresolved or ambiguous lives in one place.
/// </summary>
public static partial class ExpectationObserver
{
    private static readonly HashSet<string> NotSymbols = ["file", "use", "namespace", "doc"];
    private static readonly string[] ContentFolders = ["src", "docs", "tokensave-docs"];

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static ExpectationRecord Observe(
        TokenSaveGraph graph, string worktree, string state, string commit, ExpectationRecord? previous)
    {
        var (symbols, observed) = Extract(graph);
        var relationships = Merge(observed, previous, state).ToList();

        return new ExpectationRecord(
            1, "synthetic-csharp", state, commit, PlatformPins.TokenSave,
            Files(graph, worktree, previous), symbols, relationships, DocAssociations(graph, worktree));
    }

    /// <summary>
    /// The symbols (1-based extents) and the resolved, unresolved and ambiguous relationships of a graph. This is the one
    /// place that decides what counts as unresolved: a call to a name defined in the same project that TokenSave left
    /// without an edge or a candidate list.
    /// </summary>
    public static (List<ExpSymbol> Symbols, List<ExpRelationship> Relationships) Extract(TokenSaveGraph graph)
    {
        var symbolNodes = graph.Symbols.Where(s => !NotSymbols.Contains(s.Kind)).ToList();
        var symbols = symbolNodes
            .Select(s => new ExpSymbol(
                s.FilePath, QualifiedNames.InFile(s.FilePath, s.QualifiedName), s.Kind,
                s.Signature is null ? null : Whitespace().Replace(s.Signature, " ").Trim(),
                s.StartLine + 1, s.EndLine + 1))
            .OrderBy(s => s.Path, StringComparer.Ordinal).ThenBy(s => s.StartLine).ThenBy(s => s.QualifiedName, StringComparer.Ordinal)
            .ToList();

        var refs = BuildRefs(graph, symbolNodes);
        string Ref(string id) => refs.TryGetValue(id, out var r) ? r : $"?{id}";

        var relationships = new List<ExpRelationship>();
        relationships.AddRange(graph.Edges
            .Select(e => (e.Kind, From: Ref(e.SourceId), To: Ref(e.TargetId)))
            .Distinct()
            .Select(e => new ExpRelationship(e.From, e.To, e.Kind, "resolved")));

        var ambiguousKeys = graph.Ambiguous.Select(a => (a.FromNodeId, a.ReferenceName, a.Line)).ToHashSet();
        relationships.AddRange(graph.Ambiguous
            .Select(a => new ExpRelationship(Ref(a.FromNodeId), a.ReferenceName, "calls", "ambiguous",
                a.CandidateIds.Select(Ref).Order(StringComparer.Ordinal).ToList()))
            .DistinctBy(r => (r.From, r.To)));

        var definedNames = symbolNodes.Where(s => s.Kind != "module").Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var linkedLines = graph.Edges.Where(e => e.Line is not null).Select(e => (e.SourceId, e.Line!.Value)).ToHashSet();
        relationships.AddRange(graph.Unresolved
            .Where(u => u.ReferenceKind == "calls" && !u.ReferenceName.Contains("::") && !u.ReferenceName.StartsWith('@'))
            .Where(u => definedNames.Contains(LastSegment(u.ReferenceName)))
            .Where(u => !linkedLines.Contains((u.FromNodeId, u.Line)) && !ambiguousKeys.Contains((u.FromNodeId, u.ReferenceName, u.Line)))
            .Select(u => new ExpRelationship(Ref(u.FromNodeId), u.ReferenceName, "calls", "unresolved"))
            .DistinctBy(r => (r.From, r.To)));

        return (symbols, relationships);
    }

    /// <summary>Symbol reference: <c>path#name</c>, with <c>#k</c> when several symbols of a file share the name.</summary>
    private static Dictionary<string, string> BuildRefs(TokenSaveGraph graph, List<SymbolRow> symbolNodes)
    {
        var refs = new Dictionary<string, string>();
        foreach (var group in symbolNodes
                     .GroupBy(s => (s.FilePath, Name: QualifiedNames.InFile(s.FilePath, s.QualifiedName)))
                     .Select(g => g.OrderBy(s => s.StartLine).ThenBy(s => s.Id, StringComparer.Ordinal).ToList()))
        {
            for (var i = 0; i < group.Count; i++)
            {
                var name = QualifiedNames.InFile(group[i].FilePath, group[i].QualifiedName);
                refs[group[i].Id] = group.Count == 1 ? $"{group[i].FilePath}#{name}" : $"{group[i].FilePath}#{name}#{i}";
            }
        }
        foreach (var s in graph.Symbols.Where(s => s.Kind is "file" or "doc"))
            refs[s.Id] = s.FilePath;
        return refs;
    }

    private static string LastSegment(string reference)
    {
        var name = reference.StartsWith("new ", StringComparison.Ordinal) ? reference[4..] : reference;
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    private static IEnumerable<ExpRelationship> Merge(List<ExpRelationship> observed, ExpectationRecord? previous, string state)
    {
        var notes = previous?.Relationships.Where(r => r.Note is not null)
            .ToDictionary(r => (r.From, r.To, r.Kind, r.Status), r => r.Note) ?? [];
        var merged = observed.Select(r => notes.TryGetValue((r.From, r.To, r.Kind, r.Status), out var n) ? r with { Note = n } : r).ToList();

        // Known limitations cannot be observed, so they are carried over from the previous record of the base state.
        if (state == "base" && previous is { State: "base" })
            merged.AddRange(previous.Relationships.Where(r => r.Status == "unsupported"));

        var order = new[] { "resolved", "unresolved", "ambiguous", "unsupported" };
        return merged
            .OrderBy(r => Array.IndexOf(order, r.Status)).ThenBy(r => r.Kind, StringComparer.Ordinal)
            .ThenBy(r => r.From, StringComparer.Ordinal).ThenBy(r => r.To, StringComparer.Ordinal);
    }

    private static List<ExpFile> Files(TokenSaveGraph graph, string worktree, ExpectationRecord? previous)
    {
        var notes = previous?.Files.Where(f => f.Note is not null).ToDictionary(f => f.Path, f => f.Note) ?? [];
        var extracted = graph.Files.Where(f => f.NodeCount > 0).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var files = new List<ExpFile>();
        foreach (var folder in ContentFolders)
        {
            var root = Path.Combine(worktree, folder);
            if (!Directory.Exists(root)) continue;
            foreach (var full in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(worktree, full).Replace('\\', '/');
                var bytes = File.ReadAllBytes(full);
                files.Add(new ExpFile(rel, FileEncoding.Detect(bytes), FileEncoding.LineEnding(bytes), extracted.Contains(rel),
                    notes.GetValueOrDefault(rel)));
            }
        }
        return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
    }

    private static List<ExpDocAssociation> DocAssociations(TokenSaveGraph graph, string worktree)
    {
        var docs = graph.Symbols.Where(s => s.Kind == "doc").ToDictionary(s => s.Id, s => s.FilePath);
        var byId = graph.Symbols.ToDictionary(s => s.Id);
        var result = graph.Edges.Where(e => e.Kind == "documents" && docs.ContainsKey(e.SourceId) && byId.ContainsKey(e.TargetId))
            .GroupBy(e => docs[e.SourceId])
            .Select(g => new ExpDocAssociation(
                g.Key,
                g.Select(e => byId[e.TargetId].FilePath).Distinct().Order(StringComparer.Ordinal).ToList(),
                g.Key.EndsWith(".readme.md", StringComparison.Ordinal) ? "sidecar" : "central"))
            .ToList();

        // A central document whose applies_to matches nothing gets no association from the extractor: it is stale.
        var docsDir = Path.Combine(worktree, "tokensave-docs");
        if (Directory.Exists(docsDir))
        {
            foreach (var file in Directory.EnumerateFiles(docsDir, "*.md", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(worktree, file).Replace('\\', '/');
                if (result.Any(r => r.Doc == rel)) continue;
                var declared = AppliesTo(File.ReadAllText(file));
                if (declared.Count > 0)
                    result.Add(new ExpDocAssociation(rel, declared, "central", Stale: true));
            }
        }
        return result.OrderBy(r => r.Doc, StringComparer.Ordinal).ToList();
    }

    /// <summary>The <c>applies_to</c> list of a Markdown front matter block.</summary>
    internal static List<string> AppliesTo(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---") return [];
        var result = new List<string>();
        var inList = false;
        foreach (var line in lines.Skip(1))
        {
            if (line.Trim() == "---") break;
            if (line.StartsWith("applies_to:", StringComparison.Ordinal)) { inList = true; continue; }
            if (inList && line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                result.Add(line.TrimStart()[2..].Trim().Trim('"', '\''));
            else if (inList && line.Trim().Length > 0) inList = false;
        }
        return result;
    }
}

public static class FileEncoding
{
    public static string Detect(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return "utf-8-bom";
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return "utf-16le";
        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return "utf-8";
        }
        catch (DecoderFallbackException)
        {
            return "latin-1";
        }
    }

    public static string LineEnding(byte[] bytes)
    {
        // UTF-16 stores a line feed as 0A 00, so counting raw bytes still works for its CR LF / LF pairs.
        var lf = 0;
        var crlf = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n') continue;
            lf++;
            if (i > 0 && bytes[i - 1] == (byte)'\r') crlf++;
        }
        return crlf == 0 ? "lf" : crlf == lf ? "crlf" : "mixed";
    }

    public static string Decode(byte[] bytes) => Detect(bytes) switch
    {
        "utf-8-bom" => new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3),
        "utf-16le" => Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2),
        "latin-1" => Encoding.Latin1.GetString(bytes),
        _ => new UTF8Encoding(false).GetString(bytes),
    };
}

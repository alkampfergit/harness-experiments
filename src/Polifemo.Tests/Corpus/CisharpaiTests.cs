using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Polifemo.Contracts.Identity;
using Polifemo.Platform.Fixtures;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Corpus;

[Trait("Category", "Corpus")]
[Trait("Category", "TokenSave")]
public class CisharpaiTests(CisharpaiCorpus corpus) : IClassFixture<CisharpaiCorpus>
{
    private sealed record Curated(string Corpus, string Commit, string TokensaveVersion, List<ExpSymbol> Symbols, List<ExpRelationship> Relationships);

    private sealed record Invariants(int BandPercent, Dictionary<string, int> NodeKinds, Dictionary<string, int> EdgeKinds, int Files, int Ambiguous);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static T Read<T>(string name) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(CisharpaiCorpus.ExpectationsDir, name)), Json)!;

    private static bool UpdateSnapshots => Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";

    [Fact]
    public async Task PinnedCommitMatchesProvenance()
    {
        corpus.RequireAvailable();
        var p = CisharpaiCorpus.LoadProvenance();

        Assert.Matches("^[0-9a-f]{40}$", p.Commit);
        Assert.False(string.IsNullOrWhiteSpace(p.License));
        Assert.StartsWith("https://", p.Url);
        Assert.Equal(p.Commit, await Git.RunAsync(CisharpaiCorpus.SamplePath, ["rev-parse", "HEAD"]));

        var gitlink = await Git.RunAsync(CisharpaiCorpus.RepoRoot, ["ls-files", "-s", "testrepo/cisharpai"]);
        Assert.StartsWith($"160000 {p.Commit}", gitlink);
        Assert.Equal(p.Commit, Read<Curated>("curated.json").Commit);
    }

    [Fact]
    public async Task Preparing_and_indexing_leave_the_corpus_untouched()
    {
        corpus.RequireAvailable();

        // Ignored files may exist (an IDE restoring the projects creates obj/ folders); what matters is that preparing did not change anything.
        Assert.Equal(corpus.StatusBefore, corpus.StatusAfter);
        Assert.Equal("", await Git.RunAsync(CisharpaiCorpus.SamplePath, ["--no-optional-locks", "status", "--porcelain", "--untracked-files=all"]));
        Assert.False(Directory.Exists(Path.Combine(CisharpaiCorpus.SamplePath, ".tokensave")));
        Assert.True(Directory.Exists(Path.Combine(corpus.Worktree, ".tokensave")));
    }

    [Fact]
    public void Only_public_corpora_under_testrepo_are_committed_as_submodules()
    {
        var modules = File.ReadAllText(Path.Combine(CisharpaiCorpus.RepoRoot, ".gitmodules"));
        var paths = Regex.Matches(modules, @"path\s*=\s*(\S+)").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(paths);
        Assert.All(paths, path => Assert.StartsWith("testrepo/", path));
        Assert.DoesNotContain("samples/", modules);
    }

    [Fact]
    public void CuratedSymbolsExtracted()
    {
        corpus.RequireAvailable();
        var curated = Read<Curated>("curated.json");
        Assert.InRange(curated.Symbols.Count, 15, 60);

        var problems = new List<string>();
        foreach (var expected in curated.Symbols)
        {
            var candidates = corpus.Symbols.Where(s => s.Path == expected.Path && s.QualifiedName == expected.QualifiedName && s.Kind == expected.Kind).ToList();
            var match = candidates.FirstOrDefault(s => s.StartLine == expected.StartLine && s.EndLine == expected.EndLine);
            if (match is null)
            {
                problems.Add($"MISSING {expected.Path}#{expected.QualifiedName} ({expected.Kind}) {expected.StartLine}-{expected.EndLine}; observed: "
                             + (candidates.Count == 0 ? "none" : string.Join("; ", candidates.Select(c => $"{c.StartLine}-{c.EndLine}"))));
            }
            else if (expected.Signature is not null && match.Signature != expected.Signature)
            {
                problems.Add($"SIGNATURE {expected.Path}#{expected.QualifiedName}: expected '{expected.Signature}' but found '{match.Signature}'");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void CuratedRelationships()
    {
        corpus.RequireAvailable();
        var curated = Read<Curated>("curated.json");
        Assert.Contains(curated.Relationships, r => r.Status == "resolved");
        Assert.Contains(curated.Relationships, r => r.Status == "unresolved");
        Assert.Contains(curated.Relationships, r => r.Status == "ambiguous");

        var problems = new List<string>();
        foreach (var expected in curated.Relationships)
        {
            TestContext.Current.SendDiagnosticMessage($"[cisharpai] {expected.Status.ToUpperInvariant()} {expected.Kind} {expected.From} -> {expected.To}");
            var sameFrom = corpus.Relationships.Where(r => r.From == expected.From).ToList();
            var found = expected.Status switch
            {
                "resolved" or "unresolved" => sameFrom.Any(r => r.Status == expected.Status && r.Kind == expected.Kind && r.To == expected.To),
                "ambiguous" => sameFrom.Any(r => r.Status == "ambiguous" && r.To == expected.To
                                                 && (r.Candidates ?? []).SequenceEqual((expected.Candidates ?? []).Order(StringComparer.Ordinal))),
                "unsupported" => !sameFrom.Any(r => r.Status == "resolved" && r.Kind == expected.Kind && r.To == expected.To),
                _ => false,
            };
            if (!found)
            {
                problems.Add($"{expected.Status} {expected.Kind} {expected.From} -> {expected.To} not observed as such. Same source: "
                             + string.Join(" | ", sameFrom.Take(8).Select(r => $"{r.Status} {r.Kind} {r.To}" + (r.Candidates is { Count: > 0 } c ? $" [{string.Join(", ", c)}]" : ""))));
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private IReadOnlyList<(ExpSymbol Symbol, LogicalKey Key)> Keys()
    {
        var result = new List<(ExpSymbol, LogicalKey)>();
        foreach (var file in corpus.Symbols.GroupBy(s => s.Path))
        {
            var ordered = file.OrderBy(s => s.StartLine).ThenBy(s => s.QualifiedName, StringComparer.Ordinal).ToList();
            var keys = LogicalKeys.ForFile("cisharpai", file.Key,
                ordered.Select(s => new SymbolDescriptor(s.QualifiedName, s.Kind, s.Signature, s.StartLine)).ToList());
            result.AddRange(ordered.Zip(keys));
        }
        return result;
    }

    [Fact]
    public void LogicalKeysUnique()
    {
        corpus.RequireAvailable();
        var keys = Keys();

        Assert.True(keys.Count > 4000, $"expected thousands of symbols, found {keys.Count}");
        var duplicates = keys.GroupBy(k => k.Key).Where(g => g.Count() > 1).Select(g => g.First().Symbol).Take(5).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate logical keys, for example: " + string.Join("; ", duplicates.Select(d => $"{d.Path}#{d.QualifiedName}")));
    }

    [Fact]
    public void ExtentsWithinFileBounds()
    {
        corpus.RequireAvailable();
        var lineCounts = new Dictionary<string, int>();
        var problems = new List<string>();

        foreach (var s in corpus.Symbols)
        {
            if (!lineCounts.TryGetValue(s.Path, out var lines))
            {
                var bytes = File.ReadAllBytes(Path.Combine(corpus.Worktree, s.Path));
                lines = FileEncoding.Decode(bytes).Replace("\r\n", "\n").Split('\n').Length;
                lineCounts[s.Path] = lines;
            }
            if (s.StartLine < 1 || s.EndLine < s.StartLine || s.EndLine > lines)
                problems.Add($"{s.Path}#{s.QualifiedName}: {s.StartLine}-{s.EndLine} outside 1-{lines}");
        }
        Assert.True(problems.Count == 0, $"{problems.Count} symbols outside their file, for example:\n" + string.Join("\n", problems.Take(10)));
    }

    [Fact]
    public void CountsWithinBands()
    {
        corpus.RequireAvailable();
        var expected = Read<Invariants>("invariants.json");
        var problems = new List<string>();

        void Check(string what, int baseline, int actual)
        {
            var tolerance = Math.Max(1, (int)Math.Round(baseline * expected.BandPercent / 100.0));
            if (Math.Abs(actual - baseline) > tolerance)
                problems.Add($"{what}: expected {baseline} ±{tolerance}, found {actual}");
        }

        foreach (var (kind, count) in expected.NodeKinds) Check($"nodes[{kind}]", count, corpus.Graph.Symbols.Count(s => s.Kind == kind));
        foreach (var (kind, count) in expected.EdgeKinds) Check($"edges[{kind}]", count, corpus.Graph.Edges.Count(e => e.Kind == kind));
        Check("files", expected.Files, corpus.Graph.Files.Count);
        Check("ambiguous calls", expected.Ambiguous, corpus.Graph.Ambiguous.Count);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        // Every kind the extractor produced is covered by the bands, so a new kind cannot appear unnoticed.
        Assert.Empty(corpus.Graph.Symbols.Select(s => s.Kind).Distinct().Except(expected.NodeKinds.Keys));
        Assert.Empty(corpus.Graph.Edges.Select(e => e.Kind).Distinct().Except(expected.EdgeKinds.Keys));
    }

    private sealed record ProjectSummary(int Count, string Digest);

    private sealed record Summary(string Corpus, string Commit, string TokensaveVersion, int Total, SortedDictionary<string, ProjectSummary> Projects);

    [Fact]
    public void SummaryMatchesSnapshot()
    {
        corpus.RequireAvailable();
        var keys = Keys();
        var projects = new SortedDictionary<string, ProjectSummary>(StringComparer.Ordinal);
        foreach (var group in keys.GroupBy(k => Project(k.Symbol.Path)))
        {
            var sorted = group.Select(k => k.Key.Value).Order(StringComparer.Ordinal).ToList();
            projects[group.Key] = new ProjectSummary(sorted.Count,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", sorted)))));
        }
        var actual = new Summary("cisharpai", corpus.Provenance.Commit, PlatformPins.TokenSave, keys.Count, projects);
        var snapshotPath = Path.Combine(CisharpaiCorpus.ExpectationsDir, "summary.snapshot.json");
        var actualJson = JsonSerializer.Serialize(actual, Json) + "\n";

        if (UpdateSnapshots) File.WriteAllText(snapshotPath, actualJson);
        var expectedJson = File.Exists(snapshotPath) ? File.ReadAllText(snapshotPath) : "";

        if (expectedJson != actualJson)
        {
            File.WriteAllText(snapshotPath + ".received", actualJson);
            File.WriteAllLines(snapshotPath + ".received.keys.txt", keys.Select(k => $"{k.Symbol.Path}#{k.Symbol.QualifiedName} {k.Symbol.Kind} {k.Key}").Order(StringComparer.Ordinal));
        }
        Assert.True(expectedJson == actualJson,
            $"The extraction summary changed. Compare {snapshotPath}.received with the snapshot; UPDATE_SNAPSHOTS=1 regenerates it.");
    }

    private static string Project(string path) => path.Split('/')[0] == "src" && path.Split('/').Length > 2 ? path.Split('/')[1] : path.Split('/')[0];
}

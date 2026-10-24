using System.Text.Json;
using Json.Schema;
using Polifemo.Platform.TokenSave;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Fixtures;

public class ExpectationTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    public static TheoryData<string> States => FixtureStates.Names();

    // FromFile registers the schema's $id globally, so it must be loaded exactly once per process.
    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromFile(
        Path.Combine(TestWorkspace.RepoRoot(), "specs", "001-platform-foundations", "contracts", "expectation-record.schema.json")));

    private static bool Update => Environment.GetEnvironmentVariable("UPDATE_EXPECTATIONS") == "1";

    [Fact]
    public void Every_state_has_a_record_and_no_record_is_orphaned()
    {
        var expected = new[] { "base" }.Concat(SyntheticFixture.MutationNames()).Order(StringComparer.Ordinal).ToList();
        var actual = Directory.GetFiles(Path.Combine(SyntheticFixture.Directory, "expectations"), "*.json")
            .Select(Path.GetFileNameWithoutExtension).Cast<string>().Order(StringComparer.Ordinal).ToList();

        if (!Update) Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(States))]
    [Trait("Category", "Expectations")]
    [Trait("Category", "TokenSave")]
    public async Task Pinned_extractor_output_matches_the_recorded_expectations(string state)
    {
        RequireTokenSave.Ensure();
        var fixture = await FixtureStates.CreateAsync(_ws, state, index: true);
        var graph = await TokenSaveGraph.LoadAsync(fixture.Worktree, ct: TestContext.Current.CancellationToken);
        var path = ExpectationRecord.PathFor(state);
        var previous = File.Exists(path) ? ExpectationRecord.Load(state) : null;
        var observed = ExpectationObserver.Observe(graph, fixture.Worktree, state, fixture.Commit, previous);

        if (Update) File.WriteAllText(path, observed.ToJson());
        var expected = ExpectationRecord.Load(state);
        var result = ExpectationComparer.Compare(expected, observed);

        foreach (var line in result.Reported) TestContext.Current.SendDiagnosticMessage($"[{state}] {line}");
        Assert.True(result.Failures.Count == 0, $"{state}:\n" + string.Join("\n", result.Failures));
        Assert.Equal(fixture.Commit, expected.Commit);
        Assert.Equal(PlatformPins.TokenSave, expected.TokensaveVersion);
    }

    [Theory]
    [MemberData(nameof(States))]
    [Trait("Category", "Expectations")]
    public async Task Recorded_extents_are_correct_in_the_raw_file_bytes(string state)
    {
        var fixture = await FixtureStates.CreateAsync(_ws, state, index: false);
        var record = ExpectationRecord.Load(state);

        var problems = new List<string>();
        foreach (var symbol in record.Symbols.Where(s => s.Status != "unsupported"))
        {
            var file = Path.Combine(fixture.Worktree, symbol.Path);
            if (!File.Exists(file)) { problems.Add($"{symbol.Path} does not exist in state {state}"); continue; }

            var lines = FileEncoding.Decode(File.ReadAllBytes(file)).Replace("\r\n", "\n").Split('\n');
            if (symbol.StartLine < 1 || symbol.EndLine < symbol.StartLine || symbol.EndLine > lines.Length)
            {
                problems.Add($"{symbol.Path}#{symbol.QualifiedName}: {symbol.StartLine}-{symbol.EndLine} is outside 1-{lines.Length}");
                continue;
            }

            var name = symbol.QualifiedName.Split("::")[^1];
            var extent = string.Join("\n", lines[(symbol.StartLine - 1)..symbol.EndLine]);
            if (!extent.Contains(name, StringComparison.Ordinal))
                problems.Add($"{symbol.Path}#{symbol.QualifiedName}: lines {symbol.StartLine}-{symbol.EndLine} do not contain '{name}'");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(record.Files.Count > 0 && record.Symbols.Count > 0);
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Every_record_is_valid_against_the_schema(string state)
    {
        var schema = Schema.Value;
        using var node = JsonDocument.Parse(File.ReadAllText(ExpectationRecord.PathFor(state)));

        var result = schema.Evaluate(node.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, JsonSerializer.Serialize((result.Details ?? []).Where(d => !d.IsValid).Select(d => new { d.InstanceLocation, d.Errors })));
    }

    [Fact]
    public void Base_record_keeps_unresolved_ambiguous_and_unsupported_cases_under_their_own_status()
    {
        var record = ExpectationRecord.Load("base");

        Assert.Contains(record.Relationships, r => r.Status == "unresolved" && r.To == "\"Spring Sale\".ToSlug");
        Assert.Contains(record.Relationships, r => r.Status == "unresolved" && r.To == "services.AddOrders");
        Assert.Contains(record.Relationships, r => r.Status == "ambiguous" && r.Candidates is { Count: > 0 });
        Assert.Contains(record.Relationships, r => r.Status == "unsupported" && r.Note is not null);
        Assert.Contains(record.Files, f => f.Path.EndsWith("Utf16Marker.cs") && f.Encoding == "utf-16le");
        Assert.Contains(record.Files, f => f.Path.EndsWith("CrlfMarker.cs") && f.LineEnding == "crlf");
        Assert.Contains(record.Files, f => f.Path.EndsWith("Latin1Marker.cs") && f.Encoding == "latin-1");
        Assert.Contains(record.Files, f => f.Path.EndsWith("BomMarker.cs") && f.Encoding == "utf-8-bom");
        Assert.Contains(record.DocAssociations, d => d.Stale == true && d.Doc.EndsWith("legacy-pricing.md"));
        Assert.Contains(record.DocAssociations, d => d.Mechanism == "sidecar");
    }

    [Fact]
    public void Overloads_and_same_named_nested_types_have_distinct_symbol_references()
    {
        var record = ExpectationRecord.Load("base");
        var add = record.Symbols.Where(s => s.QualifiedName == "Money::Add").ToList();

        Assert.Equal(2, add.Count);
        Assert.NotEqual(add[0].Signature, add[1].Signature);
        Assert.Contains(record.Symbols, s => s.QualifiedName == "Cache::Entry");
        Assert.Contains(record.Symbols, s => s.QualifiedName == "Index::Entry");
        Assert.Equal(2, record.Symbols.Count(s => s.Kind == "class" && s.QualifiedName == "Order"));
    }
}

using System.Text.Json;
using Polifemo.Platform.Fixtures;
using Polifemo.Platform.TokenSave;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Corpus;

public sealed record CorpusProvenance(string Name, string Url, string Commit, string License, string LicenseEvidence, string ApprovedOn);

/// <summary>
/// The pinned public corpus <c>testrepo/cisharpai</c>, prepared (cloned and indexed) once under <c>.index/</c>.
/// Without the submodule the tests are skipped locally and fail when <c>CI</c> or <c>REQUIRE_CORPORA=1</c> is set.
/// </summary>
public sealed class CisharpaiCorpus : IAsyncLifetime
{
    private readonly TestWorkspace _ws = new();

    public static string RepoRoot => TestWorkspace.RepoRoot();
    public static string SamplePath => Path.Combine(RepoRoot, "testrepo", "cisharpai");
    public static string ExpectationsDir => Path.Combine(RepoRoot, "src", "Polifemo.Tests", "Corpus", "Expectations", "cisharpai");

    public static bool Available => Directory.Exists(Path.Combine(SamplePath, "src")) && (File.Exists(Path.Combine(SamplePath, ".git")) || Directory.Exists(Path.Combine(SamplePath, ".git")));

    private static bool Required =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) || Environment.GetEnvironmentVariable("REQUIRE_CORPORA") == "1";

    public CorpusProvenance Provenance { get; private set; } = null!;
    public string Worktree { get; private set; } = "";
    public string StatusBefore { get; private set; } = "";
    public string StatusAfter { get; private set; } = "";
    public TokenSaveGraph Graph { get; private set; } = null!;
    public IReadOnlyList<ExpSymbol> Symbols { get; private set; } = [];
    public IReadOnlyList<ExpRelationship> Relationships { get; private set; } = [];

    public static CorpusProvenance LoadProvenance()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "testrepo", "corpora.json")));
        var entry = doc.RootElement.GetProperty("corpora").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "cisharpai");
        return JsonSerializer.Deserialize<CorpusProvenance>(entry.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    public async ValueTask InitializeAsync()
    {
        Provenance = LoadProvenance();
        if (!Available)
        {
            if (Required) throw new InvalidOperationException("testrepo/cisharpai is not initialized; run: git submodule update --init testrepo/cisharpai");
            return;
        }

        RequireTokenSave.Ensure();
        const string status = "status";
        StatusBefore = await Git.RunAsync(SamplePath, ["--no-optional-locks", status, "--porcelain=v1", "--ignored", "--untracked-files=all"]);
        var prepared = await new SamplePreparer(_ws.Combine("work")).PrepareAsync(SamplePath, Provenance.Commit, "cisharpai", index: true);
        StatusAfter = await Git.RunAsync(SamplePath, ["--no-optional-locks", status, "--porcelain=v1", "--ignored", "--untracked-files=all"]);
        Worktree = prepared.WorkDir;
        Graph = await TokenSaveGraph.LoadAsync(Worktree);
        (var symbols, var relationships) = ExpectationObserver.Extract(Graph);
        Symbols = symbols;
        Relationships = relationships;
    }

    /// <summary>Skips the calling test when the corpus is absent (local runs only).</summary>
    public void RequireAvailable()
    {
        if (!Available) Assert.Skip("testrepo/cisharpai is not initialized: git submodule update --init testrepo/cisharpai");
    }

    public ValueTask DisposeAsync()
    {
        _ws.Dispose();
        return ValueTask.CompletedTask;
    }
}

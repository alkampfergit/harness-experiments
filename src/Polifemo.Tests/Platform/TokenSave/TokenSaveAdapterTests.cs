using System.Text;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.TokenSave;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.TokenSave;

public sealed class TokenSaveAdapterTests : IDisposable
{
    private const string Sentinel = "SENTINEL_ROW_CONTENT";
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private static CompatibilityChecker Checker(string binary = PlatformPins.TokenSave) =>
        new(new ActiveDatabaseResolver((_, _) => Task.FromResult<string?>("main")), _ => Task.FromResult(binary));

    private static TokenSaveAdapter Adapter(Func<int, CancellationToken, Task>? afterRowsRead = null, string binary = PlatformPins.TokenSave) =>
        new(Checker(binary), afterRowsRead);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] ABody = Encoding.UTF8.GetBytes("class A { void M() { Foo(); Bar(); } }\n");
    private static readonly byte[] BBody = Encoding.UTF8.GetBytes("class B {}\n");

    private string TokensaveDir => _ws.Combine(".tokensave");

    /// <summary>A small graph with every row kind, its source files written so their hashes match.</summary>
    private SyntheticTokenSave Populated(SyntheticTokenSave? ts = null)
    {
        SyntheticTokenSave.WriteSourceFiles(_ws.Path, new Dictionary<string, byte[]> { ["src/A.cs"] = ABody, ["src/B.cs"] = BBody });
        return (ts ?? new SyntheticTokenSave())
            .AddFile("src/A.cs", SyntheticTokenSave.ContentHashOf(ABody), nodeCount: 3)
            .AddFile("src/B.cs", SyntheticTokenSave.ContentHashOf(BBody), nodeCount: 1)
            .AddNode("fa", "file", "A.cs", "src/A.cs", "src/A.cs", 0, 0)
            .AddNode("ca", "class", "A", "src/A.cs::A", "src/A.cs", 0, 0, signature: "class A", docstring: Sentinel)
            .AddNode("ma", "method", "M", "src/A.cs::A::M", "src/A.cs", 0, 0, signature: Sentinel, parentId: "ca")
            .AddNode("cb", "class", "B", "src/B.cs::B", "src/B.cs", 0, 0)
            .AddEdge("ca", "ma", "contains", 0)
            .AddEdge("ma", "cb", "calls", 0, resolvedBy: 2)
            .AddUnresolved("ma", "Foo", "calls", 0, "src/A.cs")
            .AddAmbiguous("ma", "Bar", "src/A.cs", 0, "[\"ca\",\"cb\"]")
            .AddDoc("src/A.readme.md", "d1", "src/A.cs", "fa")
            .AddDoc("tokensave-docs/guide.md", "d2", "src/B.cs", "cb");
    }

    [Fact]
    public async Task Reads_the_database_named_by_branch_meta()
    {
        new SyntheticTokenSave().AddFile("src/Other.cs", "00").Build(_ws.Path);
        Populated(new SyntheticTokenSave { DbFile = "main.db", BranchMetaJson = SyntheticTokenSave.BranchMeta("main", ("main", "main.db")) })
            .Build(_ws.Path);

        var snapshot = await Adapter().ReadAsync(_ws.Path, ct: Ct);

        Assert.Equal(".tokensave/main.db", snapshot.Provenance.DatabasePath);
        Assert.Equal("branch-meta", snapshot.Provenance.Resolution);
        Assert.False(snapshot.Provenance.MetadataAbsent);
        Assert.Equal(["src/A.cs", "src/B.cs"], snapshot.Graph.Files.Select(f => f.Path));
    }

    [Fact]
    public async Task Absent_metadata_reads_the_single_default_database_and_reports_it()
    {
        Populated().Build(_ws.Path);

        var snapshot = await Adapter().ReadAsync(_ws.Path, ct: Ct);

        Assert.Equal("default", snapshot.Provenance.Resolution);
        Assert.True(snapshot.Provenance.MetadataAbsent);
    }

    [Fact]
    public async Task Empty_graph_is_a_valid_snapshot()
    {
        new SyntheticTokenSave().Build(_ws.Path);

        var snapshot = await Adapter().ReadAsync(_ws.Path, ct: Ct);

        Assert.Empty(snapshot.Graph.Files);
        Assert.Empty(snapshot.Graph.Symbols);
        Assert.Empty(snapshot.Graph.Edges);
        Assert.Empty(snapshot.DocAssociations);
        Assert.Equal(new IntegrityReport(0, 0, 0, 0), snapshot.Integrity);
        Assert.False(string.IsNullOrEmpty(snapshot.Revision.Value));
    }

    [Fact]
    public async Task Doc_associations_are_typed_sidecar_or_central()
    {
        Populated().Build(_ws.Path);

        var docs = (await Adapter().ReadAsync(_ws.Path, ct: Ct)).DocAssociations;

        Assert.Equal(
            [("src/A.readme.md", "src/A.cs", "sidecar"), ("tokensave-docs/guide.md", "src/B.cs", "central")],
            docs.Select(d => (d.DocPath, d.TargetPath, d.Kind)));
    }

    [Fact]
    public async Task Unresolved_and_ambiguous_rows_are_preserved_with_candidates()
    {
        Populated().Build(_ws.Path);

        var graph = (await Adapter().ReadAsync(_ws.Path, ct: Ct)).Graph;

        Assert.Equal(new UnresolvedRow("ma", "Foo", "calls", 0, "src/A.cs"), Assert.Single(graph.Unresolved));
        var ambiguous = Assert.Single(graph.Ambiguous);
        Assert.Equal("Bar", ambiguous.ReferenceName);
        Assert.Equal(["ca", "cb"], ambiguous.CandidateIds);
        Assert.Equal(2, graph.Edges.Single(e => e.Kind == "calls").ResolvedBy);
    }

    [Fact]
    public async Task Dangling_references_are_counted_not_rejected()
    {
        Populated()
            .AddEdge("ma", "missing", "calls")
            .AddUnresolved("ghost", "X", "calls", 1, "src/A.cs")
            .AddAmbiguous("ghost", "Y", "src/A.cs", 1, "[\"ca\",\"nobody\",\"none\"]")
            .Build(_ws.Path);

        var snapshot = await Adapter().ReadAsync(_ws.Path, ct: Ct);

        Assert.Equal(new IntegrityReport(DanglingEdges: 1, DanglingUnresolved: 1, DanglingAmbiguousSources: 1, UnknownCandidateIds: 2), snapshot.Integrity);
        Assert.Contains(snapshot.Graph.Edges, e => e.TargetId == "missing");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Store_is_byte_identical_after_read(bool walMode)
    {
        Populated(new SyntheticTokenSave { WalMode = walMode, BranchMetaJson = SyntheticTokenSave.BranchMeta("main", ("main", "tokensave.db")) })
            .Build(_ws.Path);
        var before = TestWorkspace.StoreDigest(TokensaveDir);

        await Adapter().ReadAsync(_ws.Path, ct: Ct);

        TestWorkspace.AssertStoreUnchanged(TokensaveDir, before);
    }

    [Fact]
    public void Store_check_rejects_a_sidecar_that_carries_data()
    {
        new SyntheticTokenSave().Build(_ws.Path);
        var before = TestWorkspace.StoreDigest(TokensaveDir);
        File.WriteAllText(Path.Combine(TokensaveDir, "tokensave.db-wal"), "frames");

        Assert.ThrowsAny<Exception>(() => TestWorkspace.AssertStoreUnchanged(TokensaveDir, before));
    }

    [Fact]
    public async Task Two_reads_of_one_revision_are_equal()
    {
        Populated().Build(_ws.Path);

        var first = await Adapter().ReadAsync(_ws.Path, ct: Ct);
        var second = await Adapter().ReadAsync(_ws.Path, ct: Ct);

        Assert.Equal(first.Revision, second.Revision);
        Assert.Equal(first.Source, second.Source);
        Assert.Equal(first.Graph.Symbols, second.Graph.Symbols);
        Assert.Equal(first.Graph.Edges, second.Graph.Edges);
        Assert.Equal(first.Graph.Files, second.Graph.Files);
        Assert.Equal(first.DocAssociations, second.DocAssociations);
    }

    [Fact]
    public async Task Provenance_has_relative_path_versions_and_attempts_1()
    {
        Populated().Build(_ws.Path);

        var p = (await Adapter().ReadAsync(_ws.Path, ct: Ct)).Provenance;

        Assert.Equal(".tokensave/tokensave.db", p.DatabasePath);
        Assert.Equal(PlatformPins.TokenSave, p.IndexVersion);
        Assert.Equal(PlatformPins.TokenSave, p.BinaryVersion);
        Assert.Equal(1, p.Attempts);
    }

    [Fact]
    public async Task Node_ids_and_lines_are_provenance_only()
    {
        // Same content under different node ids: the revision does not move, so ids are not identity.
        var a = Path.Combine(_ws.Path, "a");
        var b = Path.Combine(_ws.Path, "b");
        foreach (var (root, prefix) in new[] { (a, "x"), (b, "y") })
            new SyntheticTokenSave()
                .AddNode($"{prefix}1", "class", "A", "src/A.cs::A", "src/A.cs", 0, 2)
                .AddNode($"{prefix}2", "method", "M", "src/A.cs::A::M", "src/A.cs", 1, 1, parentId: $"{prefix}1")
                .AddEdge($"{prefix}1", $"{prefix}2", "contains", 1)
                .Build(root);

        var options = new SnapshotReadOptions { VerifySource = false };
        var first = await Adapter().ReadAsync(a, options, Ct);
        var second = await Adapter().ReadAsync(b, options, Ct);

        Assert.NotEqual(first.Graph.Symbols[0].Id, second.Graph.Symbols[0].Id);
        Assert.Equal(first.Revision, second.Revision);
        Assert.Equal(["Value"], typeof(GraphRevision).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract"));
    }
}

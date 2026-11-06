using Polifemo.Platform.TokenSave;

namespace Polifemo.Tests.Platform.TokenSave;

public sealed class GraphRevisionTests
{
    private static TokenSaveGraph Graph(string idPrefix, int endLine = 4, string hash = "aa") => new()
    {
        Files = [new FileRow("src/A.cs", hash, "code", 2)],
        Symbols =
        [
            new SymbolRow($"{idPrefix}1", "class", "A", "src/A.cs::A", "src/A.cs", 0, endLine, "class A", null, null),
            new SymbolRow($"{idPrefix}2", "method", "M", "src/A.cs::A::M", "src/A.cs", 1, 2, "void M()", "doc", $"{idPrefix}1"),
        ],
        Edges = [new EdgeRow($"{idPrefix}1", $"{idPrefix}2", "contains", 1, null)],
        Unresolved = [new UnresolvedRow($"{idPrefix}2", "Foo", "calls", 2, "src/A.cs")],
        Ambiguous = [new AmbiguousRow($"{idPrefix}2", "Bar", "src/A.cs", 2, [$"{idPrefix}1", "gone"])],
        DatabasePath = "/x/tokensave.db",
    };

    private static GraphRevision Revision(TokenSaveGraph g, params DocAssociationRow[] docs) =>
        GraphRevision.Compute("7.16.0", new GraphRows(g, docs));

    [Fact]
    public void Same_content_with_different_node_ids_has_the_same_revision()
    {
        Assert.Equal(Revision(Graph("x")), Revision(Graph("y")));
    }

    [Fact]
    public void Any_content_change_changes_the_revision()
    {
        var baseline = Revision(Graph("x"));
        Assert.NotEqual(baseline, Revision(Graph("x", endLine: 5)));
        Assert.NotEqual(baseline, Revision(Graph("x", hash: "bb")));
        Assert.NotEqual(baseline, Revision(Graph("x"), new DocAssociationRow("docs/a.md", "d", "src/A.cs", "x1")));
        Assert.NotEqual(baseline, GraphRevision.Compute("7.17.0", new GraphRows(Graph("x"), [])));
    }

    [Fact]
    public void Revision_is_a_sha256_hex_value()
    {
        Assert.Matches("^[0-9a-f]{64}$", Revision(Graph("x")).Value);
    }

    [Fact]
    public void Source_fingerprint_depends_on_paths_and_hashes_only()
    {
        var a = SourceFingerprint.Compute("7.16.0", [new FileRow("b", "2", "code", 1), new FileRow("a", "1", "code", 1)]);
        var b = SourceFingerprint.Compute("7.16.0", [new FileRow("a", "1", "artifact", 9), new FileRow("b", "2", "code", 0)]);
        var c = SourceFingerprint.Compute("7.16.0", [new FileRow("a", "1", "code", 1), new FileRow("b", "3", "code", 1)]);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Theory]
    [InlineData("src/A.readme.md", "sidecar")]
    [InlineData("src/A.README.MD", "sidecar")]
    [InlineData("tokensave-docs/a.md", "central")]
    public void Doc_kind_follows_the_file_name(string docPath, string kind)
    {
        Assert.Equal(kind, new DocAssociationRow(docPath, "d", "src/A.cs", "t").Kind);
    }

    [Fact]
    public void Read_options_default_to_five_seconds_and_source_verification()
    {
        var options = new SnapshotReadOptions();
        Assert.Equal(TimeSpan.FromSeconds(5), options.LockTimeout);
        Assert.True(options.VerifySource);
    }
}

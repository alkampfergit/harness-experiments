using System.Text;
using Polifemo.Platform.TokenSave;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.TokenSave;

public sealed class SourceVerifierTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private static readonly byte[] Body = Encoding.UTF8.GetBytes("class A {}\n");

    private FileRow Write(string rel, byte[] bytes)
    {
        SyntheticTokenSave.WriteSourceFiles(_ws.Path, new Dictionary<string, byte[]> { [rel] = bytes });
        return new FileRow(rel, SyntheticTokenSave.ContentHashOf(bytes), "code", 1);
    }

    [Fact]
    public void Matching_files_verify()
    {
        var rows = new[] { Write("src/A.cs", Body), Write("src/B.cs", "b"u8.ToArray()) };

        var result = SourceVerifier.Verify(_ws.Path, rows, TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Empty(result.AllPaths);
    }

    [Fact]
    public void Changed_and_missing_files_are_reported_by_relative_path()
    {
        var changed = Write("src/A.cs", Body) with { ContentHash = new string('0', 64) };
        var missing = new FileRow("src/Gone.cs", new string('1', 64), "code", 1);

        var result = SourceVerifier.Verify(_ws.Path, [changed, missing], TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(["src/Gone.cs"], result.MissingPaths);
        Assert.Equal(["src/A.cs"], result.ChangedPaths);
        Assert.Equal(["src/A.cs", "src/Gone.cs"], result.AllPaths);
    }

    [Fact]
    public void A_path_outside_the_worktree_is_never_read_and_counts_as_missing()
    {
        File.WriteAllBytes(_ws.Combine("..", "outside.cs"), Body);
        try
        {
            var row = new FileRow("../outside.cs", SyntheticTokenSave.ContentHashOf(Body), "code", 1);
            var result = SourceVerifier.Verify(_ws.Path, [row], TestContext.Current.CancellationToken);
            Assert.Equal(["../outside.cs"], result.MissingPaths);
        }
        finally
        {
            File.Delete(_ws.Combine("..", "outside.cs"));
        }
    }

    [Fact]
    public void Cancellation_is_honoured()
    {
        var rows = new[] { Write("src/A.cs", Body) };
        Assert.Throws<OperationCanceledException>(() => SourceVerifier.Verify(_ws.Path, rows, new CancellationToken(true)));
    }
}

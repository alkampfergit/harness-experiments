using System.Text.RegularExpressions;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.Fixtures;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Fixtures;

public sealed partial class FixtureBuilderTests : IDisposable
{
    [GeneratedRegex(@"public partial class Order")]
    private static partial Regex PartialOrder();

    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Building_twice_gives_the_same_commit_and_tree_as_recorded()
    {
        var first = await SyntheticFixture.BuildAsync(_ws, "a");
        var second = await SyntheticFixture.BuildAsync(_ws, "b");

        Assert.Equal(first.Commit, second.Commit);
        Assert.Equal(first.TreeSha, second.TreeSha);
        Assert.Equal(SyntheticFixture.RecordedCommit(), first.Commit);
    }

    [Fact]
    public async Task Rebuilding_over_an_existing_output_is_still_identical()
    {
        var first = await SyntheticFixture.BuildAsync(_ws, "same");
        var again = await SyntheticFixture.BuildAsync(_ws, "same");
        Assert.Equal(first.Commit, again.Commit);
    }

    [Fact]
    public async Task Only_content_folders_become_repository_content()
    {
        var built = await SyntheticFixture.SharedAsync();
        var files = (await Git.RunAsync(built.RepositoryPath, ["ls-files"], ct: TestContext.Current.CancellationToken)).Split('\n');

        Assert.All(files, f => Assert.Matches(@"^(src|docs|tokensave-docs)/", f));
        Assert.DoesNotContain(files, f => f.Contains("mutations") || f.EndsWith("PROVENANCE.md"));
        Assert.Contains("src/Domain/Money.cs", files);
    }

    [Theory]
    [InlineData("src/Infrastructure/BomMarker.cs")]
    [InlineData("src/Infrastructure/CrlfMarker.cs")]
    [InlineData("src/Infrastructure/Latin1Marker.cs")]
    [InlineData("src/Infrastructure/Utf16Marker.cs")]
    public async Task Encodings_and_line_endings_survive_git_byte_for_byte(string path)
    {
        var built = await SyntheticFixture.SharedAsync();
        var original = Path.Combine(SyntheticFixture.Directory, path);

        var expectedBlob = await Git.RunAsync(built.RepositoryPath, ["hash-object", original], ct: TestContext.Current.CancellationToken);
        var committedBlob = await Git.RunAsync(built.RepositoryPath, ["rev-parse", $"HEAD:{path}"], ct: TestContext.Current.CancellationToken);

        Assert.Equal(expectedBlob, committedBlob);
        Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(Path.Combine(built.RepositoryPath, path)));
    }

    [Fact]
    public async Task Output_outside_an_index_folder_is_refused()
    {
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() =>
            FixtureBuilder.BuildAsync(SyntheticFixture.Directory, Path.Combine(Path.GetTempPath(), "polifemo-not-index"), ct: TestContext.Current.CancellationToken));
        Assert.Equal("FIX_BAD_OUT", ex.Code);
    }

    [Fact]
    public async Task Missing_fixture_directory_is_refused()
    {
        var ex = await Assert.ThrowsAsync<FixturePreparationException>(() =>
            FixtureBuilder.BuildAsync(_ws.Combine("nope"), _ws.Combine("out"), ct: TestContext.Current.CancellationToken));
        Assert.Equal("FIX_NOT_FOUND", ex.Code);
    }

    [Fact]
    public void Fixture_covers_the_required_csharp_and_documentation_features()
    {
        string Read(string rel) => File.ReadAllText(Path.Combine(SyntheticFixture.Directory, rel));
        var all = string.Join("\n", Directory.EnumerateFiles(Path.Combine(SyntheticFixture.Directory, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("Utf16Marker.cs")).Select(File.ReadAllText));

        Assert.Matches(@"public Money Add\(Money other\)", all);
        Assert.Matches(@"public Money Add\(decimal amount\)", all);                 // overloads
        Assert.Matches(@"interface IRepository<T>", all);                           // generics
        Assert.Matches(@"class Cache<TKey>[\s\S]*class Entry<TValue>[\s\S]*class Index<TKey>[\s\S]*class Entry<TValue>", all); // same simple name
        Assert.Equal(2, PartialOrder().Count(all));   // partial class
        Assert.Matches(@"ToSlug\(this string value\)", all);                        // extension method
        Assert.Matches(@"interface IOrderService", all);                            // interface
        Assert.Matches(@"services\.AddScoped<IOrderService, OrderService>\(\)", all); // DI registration
        Assert.Matches(@"Task PlaceOrderAsync\(Order order, CancellationToken cancellationToken\)", all); // async + cancellation
        Assert.True(File.Exists(Path.Combine(SyntheticFixture.Directory, "src/Domain/Order.readme.md"))); // sidecar
        Assert.Contains("applies_to:", Read("tokensave-docs/orders-overview.md"));                         // central, explicit
        Assert.Contains("src/Domain/Pricing.cs", Read("tokensave-docs/legacy-pricing.md"));                // stale: file is gone
        Assert.False(File.Exists(Path.Combine(SyntheticFixture.Directory, "src/Domain/Pricing.cs")));

        var bom = File.ReadAllBytes(Path.Combine(SyntheticFixture.Directory, "src/Infrastructure/BomMarker.cs"));
        var crlf = File.ReadAllText(Path.Combine(SyntheticFixture.Directory, "src/Infrastructure/CrlfMarker.cs"));
        var latin1 = File.ReadAllBytes(Path.Combine(SyntheticFixture.Directory, "src/Infrastructure/Latin1Marker.cs"));
        var utf16 = File.ReadAllBytes(Path.Combine(SyntheticFixture.Directory, "src/Infrastructure/Utf16Marker.cs"));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bom[..3]);
        Assert.Contains("\r\n", crlf);
        Assert.Contains((byte)0xE9, latin1);
        Assert.Throws<System.Text.DecoderFallbackException>(() => new System.Text.UTF8Encoding(false, true).GetString(latin1));
        Assert.Equal(new byte[] { 0xFF, 0xFE }, utf16[..2]);
    }
}

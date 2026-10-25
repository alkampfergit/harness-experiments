using System.Text;
using Polifemo.Contracts.Hashing;
using Polifemo.Contracts.Identity;
using Polifemo.Contracts.Provenance;

namespace Polifemo.Tests.Contracts;

public class HashingTests
{
    private static readonly decimal[] Amounts = [1m, 2.5m];

    // Expected values were computed independently in Python from contracts/identities-and-hashes.md.
    private const string Lk = "e522b17ada4a1fbfb6ba3b1f0a72f5947afb16990ceb003e4a14a8699b884bca";

    [Fact]
    public void Length_prefixes_prevent_concatenation_collisions()
    {
        Assert.Equal("2b7bed60258c1e767366d63b9750bf94796c364abce6436e0f0c644a9ac9a100", Hashing.Hash("t", "ab", "c"));
        Assert.Equal("c18bde3ea5be7420c0f96c39edf0f4b58bde11df28268ff24edd466541185213", Hashing.Hash("t", "a", "bc"));
    }

    [Fact]
    public void Encoding_is_a_four_byte_big_endian_length_before_each_field_including_the_tag()
    {
        var bytes = Hashing.Encode("t", "ab", new byte[] { 1, 2, 3 });

        Assert.Equal(
            [0, 0, 0, 1, (byte)'t', 0, 0, 0, 2, (byte)'a', (byte)'b', 0, 0, 0, 3, 1, 2, 3],
            bytes);
    }

    [Fact]
    public void The_tag_is_part_of_the_hash()
    {
        Assert.NotEqual(Hashing.Hash("lk/v1", "x"), Hashing.Hash("lk/v2", "x"));
    }

    [Fact]
    public void Hash_is_lowercase_hex_sha256()
    {
        var h = Hashing.Hash("t", "x");
        Assert.Equal(64, h.Length);
        Assert.Matches("^[0-9a-f]{64}$", h);
    }

    [Fact]
    public void Logical_key_known_answer()
    {
        Assert.Equal(Lk, LogicalKey.Create("repo", "src/A.cs", "A::M", "method", "void M()").Value);
        Assert.Equal("a6306313e675d0c4754ae58ee34c04df40d6d0b5424501113b54f1fbac5a2a8e",
            LogicalKey.Create("repo", "src/A.cs", "A::M", "method", "void M()", ordinal: 1).Value);
    }

    [Fact]
    public void File_hash_known_answers_and_independence_from_everything_but_the_bytes()
    {
        Assert.Equal("7a675754e1a16eaae1d8247b3796b9f6e3f63c0a9b43c84ccc5870f97fcf8108", FileHash.Create("hello"u8).Value);
        Assert.Equal("b700e0b0bcc0af146f679d195b82f1b78df85013cd8179d4249973a0cd7a40bd", FileHash.Create([]).Value);
        Assert.NotEqual(FileHash.Create("hello"u8), FileHash.Create("hello\n"u8));
    }

    [Fact]
    public void Occurrence_key_known_answer_and_every_scope_field_matters()
    {
        var lk = new LogicalKey(Lk);
        var scope = new SourceScope("tenant", "repo", "main", "wt1", "snap1");

        Assert.Equal("f5f3747ae2cda8b2252f1c1db75917c80c8816e1c02498633f12fa36e33a3426", OccurrenceKey.Create(lk, scope).Value);

        var variants = new[]
        {
            new SourceScope("other", "repo", "main", "wt1", "snap1"),
            new SourceScope("tenant", "other", "main", "wt1", "snap1"),
            new SourceScope("tenant", "repo", "other", "wt1", "snap1"),
            new SourceScope("tenant", "repo", "main", "other", "snap1"),
            new SourceScope("tenant", "repo", "main", "wt1", "other"),
        };
        Assert.All(variants, v => Assert.NotEqual(OccurrenceKey.Create(lk, scope), OccurrenceKey.Create(lk, v)));
    }

    [Fact]
    public void Chunk_hash_known_answer_and_canonical_json_ignores_key_order()
    {
        var lk = new LogicalKey(Lk);
        var expected = "e7ee82163b83a4e26d67475721dc1e0fe92ea15025342d339f18bffaf2c1a721";

        Assert.Equal(expected, ChunkHash.Create("chunker-1", "mapping-1", lk, new { b = 1, a = "x" }).Value);
        Assert.Equal(expected, ChunkHash.Create("chunker-1", "mapping-1", lk, new { a = "x", b = 1 }).Value);
        Assert.NotEqual(expected, ChunkHash.Create("chunker-1", "mapping-1", lk, new { a = "x", b = 2 }).Value);
    }

    [Fact]
    public void Embedding_input_hash_known_answer_and_it_has_no_path_or_scope_input()
    {
        var h = EmbeddingInputHash.Create("profile-1", "tmpl-1", "document", "function M"u8);

        Assert.Equal("26afe8d567d42fc1c9936ae3d78b1de9bafa7d58bbb08e38048700268aa8abf9", h.Value);
        Assert.NotEqual(h, EmbeddingInputHash.Create("profile-1", "tmpl-1", "query", "function M"u8));
    }

    [Theory]
    [InlineData("src/A.cs", "src/A.cs")]
    [InlineData("./src/A.cs", "src/A.cs")]
    [InlineData("././src/A.cs", "src/A.cs")]
    [InlineData("src\\Sub\\A.cs", "src/Sub/A.cs")]
    [InlineData("src/Ab.cs", "src/Ab.cs")]
    public void Path_is_relative_with_forward_slashes(string input, string expected) =>
        Assert.Equal(expected, Normalize.Path(input));

    [Fact]
    public void Path_keeps_case_and_normalizes_unicode_to_nfc()
    {
        Assert.NotEqual(Normalize.Path("src/A.cs"), Normalize.Path("src/a.cs"));
        Assert.Equal(Normalize.Path("src/caf\u00e9.cs"), Normalize.Path("src/cafe\u0301.cs"));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\x\\a.cs")]
    [InlineData("../a.cs")]
    [InlineData("src/../../a.cs")]
    [InlineData("")]
    public void Paths_outside_the_repository_root_are_rejected(string path) =>
        Assert.Throws<ArgumentException>(() => Normalize.Path(path));

    [Theory]
    [InlineData("public  Money   Add(Money other)", "public Money Add(Money other)")]
    [InlineData("  void M()\n", "void M()")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Signature_collapses_whitespace(string? input, string expected) =>
        Assert.Equal(expected, Normalize.Signature(input));

    [Theory]
    [InlineData("public Money Add(Money other) => new(Amount + other.Amount, Currency)", "public Money Add(Money other)")]
    [InlineData("public Money Add(Money other) => new(decimal.Round(Amount + other.Amount, 2), Currency)", "public Money Add(Money other)")]
    [InlineData("void M(Func<int, int> f = null)", "void M(Func<int, int> f = null)")]
    [InlineData("void M(Func<int, int> f)", "void M(Func<int, int> f)")]
    [InlineData("int M(string value = \"(\") => 1", "int M(string value = \"(\")")]
    [InlineData("int M(char c = '(') => 1", "int M(char c = '(')")]
    [InlineData("int M(string v = @\"a\"\"(\") => 1", "int M(string v = @\"a\"\"(\")")]
    [InlineData("[Attr(\"[\")] int M() => 1", "[Attr(\"[\")] int M()")]
    public void Signature_drops_an_expression_body_so_that_editing_a_body_keeps_the_identity(string input, string expected) =>
        Assert.Equal(expected, Normalize.Signature(input));

    [Fact]
    public void Canonical_json_sorts_keys_and_writes_numbers_in_the_invariant_culture()
    {
        Assert.Equal("{\"a\":[1,2.5],\"b\":{\"x\":null,\"y\":true},\"c\":\"s\"}",
            CanonicalJson.Serialize(new { c = "s", b = new { y = true, x = (string?)null }, a = Amounts }));

        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("it-IT");
            Assert.Equal("{\"n\":2.5}", CanonicalJson.Serialize(new { n = 2.5m }));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Utf8_is_used_for_text_fields()
    {
        Assert.Equal(Hashing.Hash("t", Encoding.UTF8.GetBytes("é")), Hashing.Hash("t", "é"));
    }
}

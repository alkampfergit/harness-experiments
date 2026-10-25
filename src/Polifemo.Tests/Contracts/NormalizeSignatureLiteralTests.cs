using Polifemo.Contracts.Hashing;

namespace Polifemo.Tests.Contracts;

/// <summary>The expression-body cut must ignore "=>" inside literals and comments, and survive unterminated ones.</summary>
public class NormalizeSignatureLiteralTests
{
    [Theory]
    [InlineData("T M(char c = '=') => 1", "T M(char c = '=')")]
    [InlineData("T M(char c = '\\'') => 1", "T M(char c = '\\'')")]
    [InlineData("T M(string s = @\"a\"\"=>b\") => 1", "T M(string s = @\"a\"\"=>b\")")]
    [InlineData("T M(string s = \"a\\\"=>b\") => 1", "T M(string s = \"a\\\"=>b\")")]
    [InlineData("T M(string s = $\"{x}=>\") => 1", "T M(string s = $\"{x}=>\")")]
    [InlineData("T M(string s = \"\"\"a=>b\"\"\") => 1", "T M(string s = \"\"\"a=>b\"\"\")")]
    [InlineData("T M() /* => */ => 1", "T M() /* => */")]
    [InlineData("T M() // => x\n => 1", "T M() // => x")]
    public void Arrow_inside_a_literal_or_comment_is_not_the_body(string input, string expected) =>
        Assert.Equal(expected, Normalize.Signature(input));

    [Theory]
    [InlineData("T M(char c = 'a => 1")]
    [InlineData("T M(string s = \"a => 1")]
    [InlineData("T M(string s = @\"a => 1")]
    [InlineData("T M(string s = \"\"\"a => 1")]
    [InlineData("T M() /* a => 1")]
    public void Unterminated_literal_swallows_the_rest_so_nothing_is_cut(string input) =>
        Assert.Equal(System.Text.RegularExpressions.Regex.Replace(input, @"\s+", " ").Trim(), Normalize.Signature(input));
}

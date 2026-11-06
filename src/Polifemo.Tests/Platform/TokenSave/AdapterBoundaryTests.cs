using System.Text.RegularExpressions;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.TokenSave;

/// <summary>No component outside the adapter knows TokenSave's tables or its reader (FR-001, contract "Public surface").</summary>
public sealed partial class AdapterBoundaryTests
{
    [GeneratedRegex(@"FROM\s+(nodes|edges|files|unresolved_refs|ambiguous_calls|metadata)\b|\bTokenSaveReader\b")]
    private static partial Regex TokenSaveKnowledge();

    private static readonly string[] Exempt =
    [
        "src/Polifemo.Platform/TokenSave/",
        "src/Polifemo.Tests/Platform/TokenSave/",
        "src/Polifemo.Tests/Support/SyntheticTokenSave.cs",
    ];

    [Fact]
    public void No_TokenSave_table_or_reader_is_named_outside_the_adapter()
    {
        var root = TestWorkspace.RepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: Path.GetRelativePath(root, f).Replace('\\', '/')))
            .Where(f => !f.Rel.Contains("/bin/", StringComparison.Ordinal) && !f.Rel.Contains("/obj/", StringComparison.Ordinal))
            .Where(f => !Exempt.Any(e => f.Rel.StartsWith(e, StringComparison.Ordinal)))
            .Where(f => TokenSaveKnowledge().IsMatch(File.ReadAllText(f.Full)))
            .Select(f => f.Rel)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_scan_sees_the_adapter_folder_it_exempts()
    {
        var adapter = Path.Combine(TestWorkspace.RepoRoot(), "src", "Polifemo.Platform", "TokenSave", "TokenSaveReader.cs");
        Assert.Matches(TokenSaveKnowledge(), File.ReadAllText(adapter));
    }
}

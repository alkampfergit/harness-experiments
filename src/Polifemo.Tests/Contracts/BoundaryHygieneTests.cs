using System.Reflection;
using System.Text.RegularExpressions;
using Polifemo.Contracts.Errors;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Contracts;

/// <summary>FR-028: boundaries are written contracts only. Nothing is implemented or abstracted before it is used.</summary>
public class BoundaryHygieneTests
{
    private static readonly string[] BoundaryNames =
        ["Exporter", "Chunker", "Store", "Embedder", "Retriever", "Fusion", "Reranker", "ContextSelector"];

    private static IEnumerable<Assembly> ProductAssemblies =>
        [typeof(PolifemoException).Assembly, typeof(PlatformPins).Assembly, typeof(Polifemo.Cli.CliApp).Assembly];

    [Fact]
    public void No_boundary_interface_exists_in_code()
    {
        var interfaces = ProductAssemblies.SelectMany(a => a.GetTypes()).Where(t => t.IsInterface).Select(t => t.Name).ToList();

        Assert.Empty(interfaces);
        var typeNames = ProductAssemblies.SelectMany(a => a.GetTypes()).Select(t => t.Name).ToList();
        Assert.DoesNotContain(typeNames, name => BoundaryNames.Any(b => name == b || name == "I" + b));
    }

    [Fact]
    public void Every_public_type_of_the_contracts_library_is_used_somewhere_else()
    {
        var root = Path.Combine(TestWorkspace.RepoRoot(), "src");
        var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}fixtures{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => f, File.ReadAllText);

        var unused = new List<string>();
        foreach (var type in typeof(PolifemoException).Assembly.GetExportedTypes().Where(t => !t.IsNested))
        {
            var name = type.Name.Split('`')[0];
            var pattern = new Regex($@"\b{Regex.Escape(name)}\b");
            var definedIn = sources.Where(kv => Regex.IsMatch(kv.Value, $@"\b(class|struct|record|enum|interface)\s+{Regex.Escape(name)}\b")).Select(kv => kv.Key).ToList();
            var used = sources.Any(kv => !definedIn.Contains(kv.Key) && pattern.IsMatch(kv.Value));
            if (!used) unused.Add(type.FullName!);
        }

        Assert.True(unused.Count == 0, "Types in Polifemo.Contracts that nothing else uses: " + string.Join(", ", unused));
    }

    [Fact]
    public void The_contracts_library_depends_on_neither_sqlite_nor_elasticsearch_nor_the_platform()
    {
        var references = typeof(PolifemoException).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain(references, n => n!.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, n => n!.Contains("Elastic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Polifemo.Platform", references);
    }

    [Fact]
    public void Chunking_and_identity_types_carry_no_provider_specific_names()
    {
        var names = typeof(PolifemoException).Assembly.GetExportedTypes()
            .SelectMany(t => new[] { t.Name }.Concat(t.GetProperties().Select(p => p.Name)))
            .ToList();

        Assert.DoesNotContain(names, n => Regex.IsMatch(n, "Elastic|Sqlite|Lucene|Pragma|Bm25|Shard|Endpoint", RegexOptions.IgnoreCase));
    }
}

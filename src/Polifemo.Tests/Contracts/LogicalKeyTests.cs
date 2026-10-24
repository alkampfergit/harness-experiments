using Polifemo.Contracts.Identity;

namespace Polifemo.Tests.Contracts;

public class LogicalKeyTests
{
    private const string Repo = "repo";

    private static IReadOnlyList<LogicalKey> Keys(string path, params SymbolDescriptor[] symbols) =>
        LogicalKeys.ForFile(Repo, path, symbols);

    [Fact]
    public void Overloads_get_different_keys_that_are_stable_across_runs()
    {
        var overloads = new[]
        {
            new SymbolDescriptor("Money::Add", "method", "public Money Add(Money other) => new(1)", 17),
            new SymbolDescriptor("Money::Add", "method", "public Money Add(decimal amount) => new(2)", 19),
        };

        var first = Keys("src/Money.cs", overloads);
        var second = Keys("src/Money.cs", overloads);

        Assert.NotEqual(first[0], first[1]);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Line_numbers_are_not_inputs()
    {
        var before = Keys("src/A.cs", new SymbolDescriptor("A::M", "method", "void M()", 10));
        var after = Keys("src/A.cs", new SymbolDescriptor("A::M", "method", "void M()", 500));
        Assert.Equal(before, after);
    }

    [Fact]
    public void Editing_an_expression_body_keeps_the_key()
    {
        var before = Keys("src/A.cs", new SymbolDescriptor("A::M", "method", "public int M() => 1", 3));
        var after = Keys("src/A.cs", new SymbolDescriptor("A::M", "method", "public int M() => decimal.Round(2)", 3));
        Assert.Equal(before, after);
    }

    [Fact]
    public void Changing_the_parameters_changes_the_key()
    {
        var a = Keys("src/A.cs", new SymbolDescriptor("A::M", "method", "void M(int x)", 3));
        var b = Keys("src/A.cs", new SymbolDescriptor("A::M", "method", "void M(long x)", 3));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Symbols_that_are_identical_in_one_file_get_an_ordinal_in_source_order()
    {
        var symbols = new[]
        {
            new SymbolDescriptor("Local", "function", "void Local()", 30),
            new SymbolDescriptor("Local", "function", "void Local()", 10),
            new SymbolDescriptor("Other", "function", "void Other()", 20),
        };

        var keys = Keys("src/A.cs", symbols);

        Assert.Equal(3, keys.Distinct().Count());
        // The second listed symbol is first in the source, so it takes ordinal 0.
        Assert.Equal(LogicalKey.Create(Repo, "src/A.cs", "Local", "function", "void Local()", 0), keys[1]);
        Assert.Equal(LogicalKey.Create(Repo, "src/A.cs", "Local", "function", "void Local()", 1), keys[0]);
        Assert.Equal(LogicalKey.Create(Repo, "src/A.cs", "Other", "function", "void Other()", null), keys[2]);
    }

    [Fact]
    public void A_unique_symbol_has_no_ordinal()
    {
        var key = Keys("src/A.cs", new SymbolDescriptor("A", "class", "class A", 1)).Single();
        Assert.Equal(LogicalKey.Create(Repo, "src/A.cs", "A", "class", "class A"), key);
    }

    [Fact]
    public void Inserting_an_earlier_duplicate_moves_the_ordinals_a_documented_trade_off()
    {
        var original = Keys("src/A.cs",
            new SymbolDescriptor("Local", "function", "void Local()", 10),
            new SymbolDescriptor("Local", "function", "void Local()", 20));
        var inserted = Keys("src/A.cs",
            new SymbolDescriptor("Local", "function", "void Local()", 5),
            new SymbolDescriptor("Local", "function", "void Local()", 10),
            new SymbolDescriptor("Local", "function", "void Local()", 20));

        // The keys follow the ordinal, not the code: the old second symbol now holds the key of ordinal 2.
        Assert.Equal(original[0], inserted[0]);
        Assert.Equal(original[1], inserted[1]);
        Assert.NotEqual(original[1], inserted[2]);
    }

    [Fact]
    public void Partial_class_fragments_in_different_files_do_not_collide()
    {
        var one = Keys("src/Order.cs", new SymbolDescriptor("Order", "class", "public partial class Order", 3)).Single();
        var two = Keys("src/Order.Totals.cs", new SymbolDescriptor("Order", "class", "public partial class Order", 2)).Single();
        Assert.NotEqual(one, two);
    }

    [Fact]
    public void Nested_generic_types_with_the_same_simple_name_do_not_collide()
    {
        var keys = Keys("src/Collections.cs",
            new SymbolDescriptor("Cache::Entry", "inner_class", "public class Entry<TValue>", 4),
            new SymbolDescriptor("Index::Entry", "inner_class", "public class Entry<TValue>", 14));
        Assert.NotEqual(keys[0], keys[1]);
    }

    [Fact]
    public void Repository_path_and_kind_all_matter_and_a_rename_changes_the_key()
    {
        var baseKey = LogicalKey.Create("repo", "src/A.cs", "A", "class", "class A");
        Assert.NotEqual(baseKey, LogicalKey.Create("other", "src/A.cs", "A", "class", "class A"));
        Assert.NotEqual(baseKey, LogicalKey.Create("repo", "src/B.cs", "A", "class", "class A"));
        Assert.NotEqual(baseKey, LogicalKey.Create("repo", "src/A.cs", "A", "record", "class A"));
        Assert.Equal(baseKey, LogicalKey.Create("repo", ".\\src\\A.cs", "A", "class", "class  A"));
    }
}

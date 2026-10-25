using Polifemo.Tests.Support;

namespace Polifemo.Tests.Contracts;

/// <summary>
/// The mutation × identity matrix of data-model.md (SC-006): for each mutation, which values of the touched
/// symbols stay and which change, and that untouched symbols keep every value.
/// </summary>
[Trait("Category", "Identity")]
public sealed class IdentityMatrixTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    public static TheoryData<string> States => FixtureStates.Names();

    private sealed record Pair(IReadOnlyList<DerivedSymbol> Base, IReadOnlyList<DerivedSymbol> Mutated);

    private async Task<Pair> PairAsync(string mutation)
    {
        var baseState = await FixtureStates.CreateAsync(_ws, "base", index: false);
        var mutated = await FixtureStates.CreateAsync(_ws, mutation, index: false);
        return new Pair(
            IdentityDeriver.Derive(ExpectationRecord.Load("base"), baseState.Worktree),
            IdentityDeriver.Derive(ExpectationRecord.Load(mutation), mutated.Worktree));
    }

    private static DerivedSymbol One(IEnumerable<DerivedSymbol> symbols, string path, string qualifiedName, string? signatureStart = null) =>
        symbols.Single(s => s.Path == path && s.QualifiedName == qualifiedName
            && (signatureStart is null || s.Signature.StartsWith(signatureStart, StringComparison.Ordinal)));

    private static IEnumerable<(DerivedSymbol Old, DerivedSymbol New)> Matched(Pair p, Func<DerivedSymbol, bool> inScope, Func<string, string>? mapPath = null)
    {
        mapPath ??= x => x;
        return p.Base.Where(inScope).Select(o => (o, p.Mutated.Single(n => n.Path == mapPath(o.Path) && n.MatchId == o.MatchId)));
    }

    /// <summary>Everything outside <paramref name="touched"/> must be exactly as before, in every value.</summary>
    private static void AssertUntouched(Pair p, Func<string, bool> touched)
    {
        var before = p.Base.Where(s => !touched(s.Path)).ToList();
        Assert.NotEmpty(before);
        foreach (var o in before)
            Assert.Equal(o.All, p.Mutated.Single(n => n.Path == o.Path && n.MatchId == o.MatchId).All);
        Assert.Equal(before.Count, p.Mutated.Count(n => !touched(n.Path)));
    }

    [Theory]
    [MemberData(nameof(States))]
    public async Task Logical_keys_are_unique_within_every_state(string state)
    {
        var fixture = await FixtureStates.CreateAsync(_ws, state, index: false);
        var derived = IdentityDeriver.Derive(ExpectationRecord.Load(state), fixture.Worktree);

        Assert.Equal(derived.Count, derived.Select(s => s.Key).Distinct().Count());
        Assert.Equal(derived.Count, derived.Select(s => s.Occurrence).Distinct().Count());
        Assert.Contains(derived, s => s.QualifiedName == "Cache::Entry");
    }

    [Fact]
    public async Task Overloads_partial_classes_and_nested_generics_have_their_own_keys()
    {
        var p = await PairAsync("edit-method");

        var adds = p.Base.Where(s => s.QualifiedName == "Money::Add").ToList();
        Assert.Equal(2, adds.Count);
        Assert.NotEqual(adds[0].Key, adds[1].Key);

        var orderClasses = p.Base.Where(s => s.Kind == "class" && s.QualifiedName == "Order").ToList();
        Assert.Equal(2, orderClasses.Count);
        Assert.NotEqual(orderClasses[0].Key, orderClasses[1].Key);

        Assert.NotEqual(
            One(p.Base, "src/Domain/Generics/Collections.cs", "Cache::Entry").Key,
            One(p.Base, "src/Domain/Generics/Collections.cs", "Index::Entry").Key);
    }

    [Fact]
    public async Task Add_file_adds_new_identities_and_changes_nothing_else()
    {
        var p = await PairAsync("add-file");
        const string added = "src/Domain/Discount.cs";

        var fresh = p.Mutated.Where(s => s.Path == added).ToList();
        Assert.NotEmpty(fresh);
        Assert.DoesNotContain(p.Base, s => s.Path == added);
        foreach (var s in fresh)
        {
            Assert.DoesNotContain(s.Key, p.Base.Select(b => b.Key));
            Assert.DoesNotContain(s.File, p.Base.Select(b => b.File));
            Assert.DoesNotContain(s.Occurrence, p.Base.Select(b => b.Occurrence));
            Assert.DoesNotContain(s.Chunk, p.Base.Select(b => b.Chunk));
            Assert.DoesNotContain(s.Embedding, p.Base.Select(b => b.Embedding));
        }
        AssertUntouched(p, path => path == added);
    }

    [Fact]
    public async Task Edit_method_keeps_the_key_and_occurrence_and_changes_content_and_chunk_and_embedding()
    {
        var p = await PairAsync("edit-method");
        const string file = "src/Domain/Money.cs";

        var (old, edited) = (One(p.Base, file, "Money::Add", "public Money Add(Money"), One(p.Mutated, file, "Money::Add", "public Money Add(Money"));
        Assert.Equal(old.Key, edited.Key);
        Assert.Equal(old.Occurrence, edited.Occurrence);
        Assert.NotEqual(old.File, edited.File);
        Assert.NotEqual(old.Chunk, edited.Chunk);
        Assert.NotEqual(old.Embedding, edited.Embedding);
        Assert.Equal((old.StartLine, old.EndLine), (edited.StartLine, edited.EndLine));

        // The class contains the edit, so its content-derived values change too.
        var (oldClass, newClass) = (One(p.Base, file, "Money"), One(p.Mutated, file, "Money"));
        Assert.Equal(oldClass.Key, newClass.Key);
        Assert.NotEqual(oldClass.Chunk, newClass.Chunk);

        // Other members of the file keep everything except the file revision.
        foreach (var (o, n) in Matched(p, s => s.Path == file && s.QualifiedName != "Money" && !(s.QualifiedName == "Money::Add" && s.Signature.Contains("Money other"))))
        {
            Assert.Equal((o.Key, o.Occurrence, o.Chunk, o.Embedding, o.StartLine, o.EndLine), (n.Key, n.Occurrence, n.Chunk, n.Embedding, n.StartLine, n.EndLine));
            Assert.NotEqual(o.File, n.File);
        }
        AssertUntouched(p, path => path == file);
    }

    [Fact]
    public async Task Delete_file_removes_its_identities_and_changes_nothing_else()
    {
        var p = await PairAsync("delete-file");
        const string deleted = "src/Domain/OrderStatus.cs";

        var gone = p.Base.Where(s => s.Path == deleted).ToList();
        Assert.NotEmpty(gone);
        Assert.DoesNotContain(p.Mutated, s => s.Path == deleted);
        foreach (var s in gone)
        {
            Assert.DoesNotContain(s.Key, p.Mutated.Select(m => m.Key));
            Assert.DoesNotContain(s.Occurrence, p.Mutated.Select(m => m.Occurrence));
        }
        AssertUntouched(p, path => path == deleted);
    }

    [Fact]
    public async Task Rename_gives_a_new_key_and_occurrence_but_keeps_the_content_revision_and_the_embedding_input()
    {
        var p = await PairAsync("rename-file");
        const string from = "src/Domain/OrderLine.cs";
        const string to = "src/Domain/Lines/OrderLine.cs";

        var moved = Matched(p, s => s.Path == from, _ => to).ToList();
        Assert.Equal(2, moved.Count);
        foreach (var (o, n) in moved)
        {
            Assert.NotEqual(o.Key, n.Key);
            Assert.Equal(o.File, n.File);
            Assert.NotEqual(o.Occurrence, n.Occurrence);
            Assert.NotEqual(o.Chunk, n.Chunk);
            Assert.Equal(o.Embedding, n.Embedding);
            Assert.Equal((o.StartLine, o.EndLine), (n.StartLine, n.EndLine));
        }
        AssertUntouched(p, path => path is from or to);
    }

    [Fact]
    public async Task Line_shift_keeps_every_identity_except_the_content_revision_and_the_location()
    {
        var p = await PairAsync("line-shift");
        const string file = "src/Domain/Money.cs";

        var shifted = Matched(p, s => s.Path == file).ToList();
        Assert.NotEmpty(shifted);
        foreach (var (o, n) in shifted)
        {
            Assert.Equal(o.Key, n.Key);
            Assert.Equal(o.Occurrence, n.Occurrence);
            Assert.Equal(o.Chunk, n.Chunk);              // lines are provenance, not chunk inputs
            Assert.Equal(o.Embedding, n.Embedding);
            Assert.NotEqual(o.File, n.File);
            Assert.Equal((o.StartLine + 2, o.EndLine + 2), (n.StartLine, n.EndLine));
        }
        AssertUntouched(p, path => path == file);
    }

    [Fact]
    public async Task Docs_only_edit_changes_the_chunk_of_the_documented_symbols_and_nothing_in_the_source()
    {
        var p = await PairAsync("docs-only-edit");
        string[] documented = ["src/Services/OrderService.cs", "src/Services/IOrderService.cs", "src/Domain/Money.cs"];
        const string doc = "tokensave-docs/orders-overview.md";

        var affected = Matched(p, s => documented.Contains(s.Path)).ToList();
        Assert.NotEmpty(affected);
        foreach (var (o, n) in affected)
        {
            Assert.Equal(o.Key, n.Key);
            Assert.Equal(o.File, n.File);                // the source did not change
            Assert.Equal(o.Occurrence, n.Occurrence);
            Assert.NotEqual(o.Chunk, n.Chunk);           // the doc text is a search field
            Assert.Equal(o.Embedding, n.Embedding);      // the doc excerpt is not part of the embedding input
            Assert.Equal((o.StartLine, o.EndLine), (n.StartLine, n.EndLine));
        }
        AssertUntouched(p, path => documented.Contains(path) || path == doc);
    }

    [Fact]
    public async Task Changing_the_doc_association_changes_only_the_chunk_of_the_symbols_that_gain_or_lose_the_link()
    {
        var p = await PairAsync("change-doc-association");
        string[] relinked = ["src/Domain/Money.cs", "src/Domain/Order.cs"];   // Money.cs loses the doc, Order.cs gains it
        const string doc = "tokensave-docs/orders-overview.md";

        foreach (var (o, n) in Matched(p, s => relinked.Contains(s.Path)))
        {
            Assert.Equal(o.Key, n.Key);
            Assert.Equal(o.File, n.File);
            Assert.Equal(o.Occurrence, n.Occurrence);
            Assert.NotEqual(o.Chunk, n.Chunk);           // docLinks is a search field
            Assert.Equal(o.Embedding, n.Embedding);
            Assert.Equal((o.StartLine, o.EndLine), (n.StartLine, n.EndLine));
        }
        AssertUntouched(p, path => relinked.Contains(path) || path == doc);
    }

    [Fact]
    public async Task Rename_and_edit_in_one_step_changes_everything_that_the_edit_touched()
    {
        var p = await PairAsync("rename-and-edit");
        const string from = "src/Services/OrderFormatter.cs";
        const string to = "src/Services/Formatting/OrderFormatter.cs";

        var edited = (Old: One(p.Base, from, "OrderFormatter::Format", "public static string Format(Order order, string"),
                      New: One(p.Mutated, to, "OrderFormatter::Format", "public static string Format(Order order, string"));
        Assert.NotEqual(edited.Old.Key, edited.New.Key);
        Assert.NotEqual(edited.Old.File, edited.New.File);
        Assert.NotEqual(edited.Old.Occurrence, edited.New.Occurrence);
        Assert.NotEqual(edited.Old.Chunk, edited.New.Chunk);
        Assert.NotEqual(edited.Old.Embedding, edited.New.Embedding);

        // The overload that was only moved keeps its embedding input, like a plain rename.
        var moved = (Old: One(p.Base, from, "OrderFormatter::Format", "public static string Format(Order order)"),
                     New: One(p.Mutated, to, "OrderFormatter::Format", "public static string Format(Order order)"));
        Assert.NotEqual(moved.Old.Key, moved.New.Key);
        Assert.NotEqual(moved.Old.File, moved.New.File);
        Assert.Equal(moved.Old.Embedding, moved.New.Embedding);

        AssertUntouched(p, path => path is from or to);
    }

    [Fact]
    public async Task The_same_content_in_two_scopes_is_one_revision_but_two_occurrences()
    {
        var fixture = await FixtureStates.CreateAsync(_ws, "base", index: false);
        var record = ExpectationRecord.Load("base");
        var scopeA = IdentityDeriver.DefaultScope;
        var scopeB = new Polifemo.Contracts.Provenance.SourceScope("local", IdentityDeriver.RepositoryId, "main", "wt2", "snap2");

        var a = IdentityDeriver.Derive(record, fixture.Worktree, scopeA);
        var b = IdentityDeriver.Derive(record, fixture.Worktree, scopeB);

        Assert.Equal(a.Count, b.Count);
        foreach (var (x, y) in a.Zip(b))
        {
            Assert.Equal(x.Key, y.Key);
            Assert.Equal(x.File, y.File);
            Assert.Equal(x.Chunk, y.Chunk);
            Assert.Equal(x.Embedding, y.Embedding);
            Assert.NotEqual(x.Occurrence, y.Occurrence);
            Assert.Equal((x.StartLine, x.EndLine), (y.StartLine, y.EndLine));
        }
        Assert.Equal(a.Count * 2, a.Concat(b).Select(s => s.Occurrence).Distinct().Count());
    }
}

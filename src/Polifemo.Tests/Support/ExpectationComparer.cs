using System.Text.Json;

namespace Polifemo.Tests.Support;

public sealed record ComparisonResult(IReadOnlyList<string> Failures, IReadOnlyList<string> Reported);

/// <summary>
/// Compares a recorded expectation with an observation. Unresolved, ambiguous and unsupported cases are
/// reported under their own status and never counted as a pass or dropped (FR-017, SC-005).
/// </summary>
public static class ExpectationComparer
{
    public static ComparisonResult Compare(ExpectationRecord expected, ExpectationRecord observed)
    {
        var failures = new List<string>();
        var reported = new List<string>();

        Diff("FILE", expected.Files.Select(f => $"{f.Path} [{f.Encoding}, {f.LineEnding}, extracted={f.Extracted}]"),
            observed.Files.Select(f => $"{f.Path} [{f.Encoding}, {f.LineEnding}, extracted={f.Extracted}]"), failures);

        static string Key(ExpSymbol s) => $"{s.Path}#{s.QualifiedName} {s.Kind} {s.StartLine}-{s.EndLine} {s.Signature}";
        var expectedSymbols = expected.Symbols.Where(s => s.Status != "unsupported").ToList();
        Diff("SYMBOL", expectedSymbols.Select(Key), observed.Symbols.Select(Key), failures);
        foreach (var unsupported in expected.Symbols.Where(s => s.Status == "unsupported"))
        {
            reported.Add($"UNSUPPORTED symbol {unsupported.Path}#{unsupported.QualifiedName} ({unsupported.Kind})");
            if (observed.Symbols.Any(o => o.Path == unsupported.Path && o.QualifiedName == unsupported.QualifiedName && o.Kind == unsupported.Kind))
                failures.Add($"UNSUPPORTED symbol is now extracted: {unsupported.Path}#{unsupported.QualifiedName}; update the record.");
        }

        static string RelKey(ExpRelationship r) =>
            $"{r.Status} {r.Kind} {r.From} -> {r.To}" + (r.Candidates is { Count: > 0 } c ? $" [{string.Join(", ", c)}]" : "");
        var verified = expected.Relationships.Where(r => r.Status != "unsupported").ToList();
        // "unsupported" entries are hand-written limitations carried into the observation; they are asserted below, not diffed.
        Diff("RELATIONSHIP", verified.Select(RelKey), observed.Relationships.Where(r => r.Status != "unsupported").Select(RelKey), failures);

        foreach (var r in expected.Relationships)
        {
            if (r.Status == "resolved") continue;
            reported.Add($"{r.Status.ToUpperInvariant()} {r.Kind} {r.From} -> {r.To}" + (r.Note is null ? "" : $" ({r.Note})"));
            if (r.Status == "unsupported" && observed.Relationships.Any(o => o.Status == "resolved" && o.Kind == r.Kind && o.From == r.From && o.To == r.To))
                failures.Add($"UNSUPPORTED relationship is now resolved: {r.From} -> {r.To}; update the record.");
        }

        static string DocKey(ExpDocAssociation d) => JsonSerializer.Serialize(d);
        Diff("DOCUMENT", expected.DocAssociations.Select(DocKey), observed.DocAssociations.Select(DocKey), failures);

        return new ComparisonResult(failures, reported);
    }

    private static void Diff(string label, IEnumerable<string> expected, IEnumerable<string> observed, List<string> failures)
    {
        var e = expected.ToList();
        var o = observed.ToList();
        failures.AddRange(e.Except(o).Select(x => $"MISSING {label}: {x}"));
        failures.AddRange(o.Except(e).Select(x => $"UNEXPECTED {label}: {x}"));
        failures.AddRange(e.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => $"DUPLICATE {label} in record: {g.Key}"));
        failures.AddRange(o.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => $"DUPLICATE {label} observed: {g.Key}"));
    }
}

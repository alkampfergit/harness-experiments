using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polifemo.Tests.Support;

public sealed record ExpFile(string Path, string Encoding, string LineEnding, bool Extracted, string? Note = null);

public sealed record ExpSymbol(string Path, string QualifiedName, string Kind, string? Signature, int StartLine, int EndLine, string? Status = null);

public sealed record ExpRelationship(string From, string To, string Kind, string Status, IReadOnlyList<string>? Candidates = null, string? Note = null);

public sealed record ExpDocAssociation(string Doc, IReadOnlyList<string> AppliesTo, string? Mechanism = null, bool? Stale = null);

/// <summary>One fixture state's expected extractor output (see contracts/expectation-record.schema.json).</summary>
public sealed record ExpectationRecord(
    int SchemaVersion,
    string Fixture,
    string State,
    string Commit,
    string TokensaveVersion,
    IReadOnlyList<ExpFile> Files,
    IReadOnlyList<ExpSymbol> Symbols,
    IReadOnlyList<ExpRelationship> Relationships,
    IReadOnlyList<ExpDocAssociation> DocAssociations)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string PathFor(string state) =>
        System.IO.Path.Combine(SyntheticFixture.Directory, "expectations", state + ".json");

    public static ExpectationRecord Load(string state) =>
        JsonSerializer.Deserialize<ExpectationRecord>(File.ReadAllText(PathFor(state)), Json)
        ?? throw new InvalidOperationException($"Expectation record '{state}' is empty.");

    public string ToJson() => JsonSerializer.Serialize(this, Json) + "\n";
}

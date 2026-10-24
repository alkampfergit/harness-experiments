using System.Reflection;
using System.Text.Json;

namespace Polifemo.Platform.TokenSave;

public sealed record ExpectedColumn(string Name, string Type, bool NotNull, bool PrimaryKey);

/// <summary>The tables and columns the adapter relies on, from the embedded <c>expected-schema.v7.16.0.json</c>.</summary>
public sealed class ExpectedSchema
{
    private const string ResourceName = "expected-schema.v7.16.0.json";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string TokensaveVersion { get; }
    public IReadOnlyList<string> RequiredMetadataKeys { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<ExpectedColumn>> Tables { get; }

    private ExpectedSchema(Dto dto)
    {
        TokensaveVersion = dto.TokensaveVersion;
        RequiredMetadataKeys = dto.RequiredMetadataKeys;
        Tables = dto.Tables.ToDictionary(t => t.Key, t => (IReadOnlyList<ExpectedColumn>)t.Value);
    }

    public static ExpectedSchema Load()
    {
        using var stream = typeof(ExpectedSchema).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        var dto = JsonSerializer.Deserialize<Dto>(stream, Options)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is empty.");
        return new ExpectedSchema(dto);
    }

    private sealed record Dto(
        string TokensaveVersion,
        List<string> RequiredMetadataKeys,
        Dictionary<string, List<ExpectedColumn>> Tables);
}

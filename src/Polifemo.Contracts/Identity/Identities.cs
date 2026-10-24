using Polifemo.Contracts.Hashing;
using Polifemo.Contracts.Provenance;

namespace Polifemo.Contracts.Identity;

/// <summary>
/// Stable identity of a symbol: repository, path, qualified name, kind and a signature or ordinal
/// disambiguator. Line numbers, TokenSave node ids, scope and content are never inputs.
/// </summary>
public readonly record struct LogicalKey(string Value)
{
    public static LogicalKey Create(string repositoryId, string path, string qualifiedName, string kind, string? signature, int? ordinal = null)
    {
        var disambiguator = Normalize.Signature(signature) + (ordinal is { } n ? $"#{n}" : "");
        return new LogicalKey(Hashing.Hashing.Hash("lk/v1", repositoryId, Normalize.Path(path), qualifiedName, kind, disambiguator));
    }

    public override string ToString() => Value;
}

public sealed record SymbolDescriptor(string QualifiedName, string Kind, string? Signature, int StartLine);

public static class LogicalKeys
{
    /// <summary>
    /// Keys for all symbols of one file, in the order given. Symbols that share (qualified name, kind, signature)
    /// get a 0-based ordinal in source order. The ordinal moves when an earlier duplicate is inserted; that
    /// trade-off is accepted and tested (research R8).
    /// </summary>
    public static IReadOnlyList<LogicalKey> ForFile(string repositoryId, string path, IReadOnlyList<SymbolDescriptor> symbols)
    {
        var ordinals = new int?[symbols.Count];
        var groups = Enumerable.Range(0, symbols.Count)
            .GroupBy(i => (symbols[i].QualifiedName, symbols[i].Kind, Signature: Normalize.Signature(symbols[i].Signature)));
        foreach (var group in groups.Where(g => g.Count() > 1))
        {
            var ordered = group.OrderBy(i => symbols[i].StartLine).ThenBy(i => i).ToList();
            for (var n = 0; n < ordered.Count; n++) ordinals[ordered[n]] = n;
        }

        return symbols
            .Select((s, i) => LogicalKey.Create(repositoryId, path, s.QualifiedName, s.Kind, s.Signature, ordinals[i]))
            .ToList();
    }
}

/// <summary>Content revision: the raw bytes only, whatever the path, encoding label or scope.</summary>
public readonly record struct FileHash(string Value)
{
    public static FileHash Create(ReadOnlySpan<byte> rawBytes) => new(Hashing.Hashing.Hash("fh/v1", HashField.From(rawBytes)));

    public override string ToString() => Value;
}

/// <summary>A symbol in one source scope. The same content in two scopes gives two occurrences.</summary>
public readonly record struct OccurrenceKey(string Value)
{
    public static OccurrenceKey Create(LogicalKey logicalKey, SourceScope scope) => new(Hashing.Hashing.Hash(
        "oc/v1", logicalKey.Value, scope.AuthorizationBoundary, scope.RepositoryId, scope.BranchRef, scope.WorktreeId, scope.SnapshotId));

    public override string ToString() => Value;
}

/// <summary>
/// Everything that makes a searchable chunk differ. <c>startLine</c> and <c>endLine</c> are provenance and must
/// not be part of <paramref name="searchFieldsAndMetadata"/>.
/// </summary>
public readonly record struct ChunkHash(string Value)
{
    public static ChunkHash Create(string chunkerVersion, string mappingVersion, LogicalKey logicalKey, object searchFieldsAndMetadata) =>
        new(Hashing.Hashing.Hash("ch/v1", chunkerVersion, mappingVersion, logicalKey.Value, CanonicalJson.Serialize(searchFieldsAndMetadata)));

    public override string ToString() => Value;
}

/// <summary>Excludes path and scope, so a rename keeps it and the stored vector can be reused.</summary>
public readonly record struct EmbeddingInputHash(string Value)
{
    public static EmbeddingInputHash Create(string embeddingProfileId, string inputTemplateVersion, string inputRole, ReadOnlySpan<byte> preparedInput) =>
        new(Hashing.Hashing.Hash("eh/v1", embeddingProfileId, inputTemplateVersion, inputRole, HashField.From(preparedInput)));

    public override string ToString() => Value;
}

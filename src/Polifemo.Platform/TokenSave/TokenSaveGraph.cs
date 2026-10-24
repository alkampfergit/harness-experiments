using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.TokenSave;

public sealed record SymbolRow(
    string Id, string Kind, string Name, string QualifiedName, string FilePath,
    int StartLine, int EndLine, string? Signature, string? Docstring, string? ParentId);

public sealed record EdgeRow(string SourceId, string TargetId, string Kind, int? Line, int? ResolvedBy);

public sealed record UnresolvedRow(string FromNodeId, string ReferenceName, string ReferenceKind, int Line, string FilePath);

public sealed record AmbiguousRow(string FromNodeId, string ReferenceName, string FilePath, int Line, IReadOnlyList<string> CandidateIds);

public sealed record FileRow(string Path, string ContentHash, string Kind, int NodeCount);

/// <summary>
/// What TokenSave extracted from a worktree. It is only loaded after the compatibility check passes,
/// so no code reads <c>nodes</c> or <c>edges</c> content from an unverified database.
/// </summary>
public sealed class TokenSaveGraph
{
    public required IReadOnlyList<SymbolRow> Symbols { get; init; }
    public required IReadOnlyList<EdgeRow> Edges { get; init; }
    public required IReadOnlyList<UnresolvedRow> Unresolved { get; init; }
    public required IReadOnlyList<AmbiguousRow> Ambiguous { get; init; }
    public required IReadOnlyList<FileRow> Files { get; init; }
    public required string DatabasePath { get; init; }

    public static async Task<TokenSaveGraph> LoadAsync(
        string worktree, CompatibilityChecker? checker = null, CancellationToken ct = default)
    {
        var report = await (checker ?? new CompatibilityChecker()).CheckAsync(worktree, PlatformPins.TokenSave, ct);
        if (!report.Ok) throw new TokenSaveCompatibilityException(report.Mismatches);

        var db = await new ActiveDatabaseResolver().ResolveAsync(worktree, ct);
        using var reader = TokenSaveReader.OpenReadOnly(db.DatabasePath);
        return new TokenSaveGraph
        {
            Symbols = reader.Symbols(),
            Edges = reader.Edges(),
            Unresolved = reader.Unresolved(),
            Ambiguous = reader.Ambiguous(),
            Files = reader.Files(),
            DatabasePath = db.DatabasePath,
        };
    }
}

public static class QualifiedNames
{
    /// <summary>
    /// TokenSave writes <c>&lt;path&gt;::&lt;path&gt;::Type::member</c> for code and <c>&lt;path&gt;::Title</c> for Markdown.
    /// The in-file name is what is left after the path prefixes (observed on 7.16.0; no namespace, no generic arguments).
    /// </summary>
    public static string InFile(string path, string qualifiedName)
    {
        var prefix = path + "::";
        var name = qualifiedName;
        while (name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..];
        return name;
    }
}

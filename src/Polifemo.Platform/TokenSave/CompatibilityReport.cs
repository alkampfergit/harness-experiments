using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.TokenSave;

/// <summary>Result of the pre-import check. Warnings never change <see cref="Ok"/>.</summary>
public sealed record CompatibilityReport(
    string? TokensaveBinaryVersion,
    string? IndexVersion,
    string? DatabasePath,
    string? Resolution,
    IReadOnlyList<Mismatch> Mismatches,
    IReadOnlyList<Mismatch> Warnings)
{
    public bool Ok => Mismatches.Count == 0;
}

namespace Polifemo.Contracts.Errors;

/// <summary>Base of every typed failure. <see cref="Code"/> is stable and machine-readable.</summary>
public class PolifemoException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}

public sealed record Mismatch(string Code, string Subject, string? Expected = null, string? Actual = null);

public sealed class TokenSaveCompatibilityException(IReadOnlyList<Mismatch> mismatches)
    : PolifemoException(
        mismatches.Count > 0 ? mismatches[0].Code : "TS_INCOMPATIBLE",
        $"TokenSave is incompatible: {string.Join(", ", mismatches.Select(m => m.Code))}")
{
    public IReadOnlyList<Mismatch> Mismatches { get; } = mismatches;
}

public sealed class ActiveDatabaseResolutionException(string code, string message)
    : PolifemoException(code, message);

public sealed class SearchStoreException(string code, string message, Exception? inner = null)
    : PolifemoException(code, message, inner);

public sealed class FixturePreparationException(string code, string message)
    : PolifemoException(code, message);

public sealed class EmbeddingProfileMismatchException(string expected, string actual)
    : PolifemoException("EMB_PROFILE_MISMATCH",
        $"Query embedding profile '{actual}' does not match index profile '{expected}'.")
{
    public string Expected { get; } = expected;
    public string Actual { get; } = actual;
}

/// <summary>A privacy or local-only rule refused the operation (F02-01, F02-03). The message carries no payload.</summary>
public sealed class PrivacyPolicyException(string code, string message)
    : PolifemoException(code, message);

/// <summary>Scope or snapshot resolution failed (F02-02). Unknown and unauthorized scopes share one code on purpose.</summary>
public sealed class ScopeException(string code, string message)
    : PolifemoException(code, message);

using Polifemo.Contracts.Errors;

namespace Polifemo.Cli;

public static class ExitCodes
{
    public const int Ok = 0;
    public const int CheckFailed = 1;
    public const int InvalidArguments = 2;
    public const int Environment = 3;
    public const int Cancelled = 130;

    /// <summary>Maps a typed failure to the exit code of contracts/cli.md.</summary>
    public static int For(Exception ex) => ex switch
    {
        OperationCanceledException => Cancelled,
        UsageException => InvalidArguments,
        TokenSaveCompatibilityException => CheckFailed,
        EmbeddingProfileMismatchException => CheckFailed,
        PrivacyPolicyException => CheckFailed,
        ScopeException => CheckFailed,
        FixturePreparationException { Code: "PREP_BAD_NAME" or "MUT_BAD_PATH" or "MUT_INVALID" or "FIX_BAD_OUT" or "FIX_NOT_FOUND" } => InvalidArguments,
        FixturePreparationException f when f.Code is "PREP_NOT_A_REPO" or "PREP_UNKNOWN_COMMIT"
            or "PREP_SOURCE_MODIFIED" or "PREP_TARGET_EXISTS" or "MUT_BASE_MISMATCH" => CheckFailed,
        SearchStoreException s when s.Code == "ES_ASSERTION_FAILED" => CheckFailed,
        PolifemoException => Environment,
        _ => Environment,
    };
}

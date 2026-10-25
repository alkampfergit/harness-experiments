using Polifemo.Cli;
using Polifemo.Contracts.Errors;

namespace Polifemo.Tests.Cli;

public class RepositoryPathsTests
{
    [Fact]
    public void Root_is_the_folder_holding_the_solution_and_work_root_lives_under_dot_index()
    {
        var root = RepositoryPaths.Root();

        Assert.True(File.Exists(Path.Combine(root, "Polifemo.slnx")));
        Assert.Equal(Path.Combine(root, ".index", "work"), RepositoryPaths.WorkRoot());
    }
}

public class ExitCodeMappingTests
{
    public static TheoryData<Exception, int> Cases => new()
    {
        { new UsageException("bad"), 2 },
        { new EmbeddingProfileMismatchException("a", "b"), 1 },
        { new FixturePreparationException("PREP_BAD_NAME", "x"), 2 },
        { new FixturePreparationException("MUT_BAD_PATH", "x"), 2 },
        { new FixturePreparationException("MUT_INVALID", "x"), 2 },
        { new FixturePreparationException("FIX_BAD_OUT", "x"), 2 },
        { new FixturePreparationException("FIX_NOT_FOUND", "x"), 2 },
        { new FixturePreparationException("PREP_NOT_A_REPO", "x"), 1 },
        { new FixturePreparationException("PREP_SOURCE_MODIFIED", "x"), 1 },
        { new FixturePreparationException("PREP_TARGET_EXISTS", "x"), 1 },
        { new FixturePreparationException("MUT_BASE_MISMATCH", "x"), 1 },
        { new FixturePreparationException("PREP_INDEX_FAILED", "x"), 3 },
        { new SearchStoreException("ES_ASSERTION_FAILED", "x"), 1 },
        { new PolifemoException("OTHER", "x"), 3 },
        { new InvalidOperationException("unexpected"), 3 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Maps_exception_to_the_documented_exit_code(Exception ex, int expected) =>
        Assert.Equal(expected, ExitCodes.For(ex));
}

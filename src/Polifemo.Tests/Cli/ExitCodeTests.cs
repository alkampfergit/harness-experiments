using Polifemo.Cli;
using Polifemo.Contracts.Errors;

namespace Polifemo.Tests.Cli;

public class ExitCodeTests
{
    public static TheoryData<Exception, int> Cases => new()
    {
        { new OperationCanceledException(), 130 },
        { new TokenSaveCompatibilityException([new Mismatch("TS_TABLE_MISSING", "nodes")]), 1 },
        { new FixturePreparationException("PREP_UNKNOWN_COMMIT", "x"), 1 },
        { new ActiveDatabaseResolutionException("TS_DB_MISSING", "x"), 3 },
        { new SearchStoreException("ES_AUTH_FAILED", "x"), 3 },
        { new SearchStoreException("ES_UNREACHABLE", "x"), 3 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Maps_exception_to_exit_code(Exception ex, int expected) =>
        Assert.Equal(expected, ExitCodes.For(ex));

    [Fact]
    public async Task Failure_writes_error_json_and_no_success()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await CliOutput.RunAsync(stdout, stderr,
            () => throw new SearchStoreException("ES_AUTH_FAILED", "rejected for user elastic"));

        Assert.Equal(3, exit);
        Assert.Contains("\"ok\": false", stdout.ToString());
        Assert.Contains("ES_AUTH_FAILED", stderr.ToString());
    }

    [Fact]
    public async Task Cancellation_writes_nothing_to_stdout()
    {
        var stdout = new StringWriter();
        var exit = await CliOutput.RunAsync(stdout, new StringWriter(),
            () => throw new OperationCanceledException());

        Assert.Equal(130, exit);
        Assert.Equal("", stdout.ToString());
    }
}

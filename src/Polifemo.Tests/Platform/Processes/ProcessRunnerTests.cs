using Polifemo.Contracts.Errors;
using Polifemo.Platform.Processes;

namespace Polifemo.Tests.Platform.Processes;

public class ProcessRunnerTests
{
    [Fact]
    public async Task Child_sees_end_of_input_so_a_tool_that_reads_stdin_cannot_block_the_run()
    {
        // `cat` reads stdin until EOF. With an inherited terminal it would wait forever.
        var result = await ProcessRunner.RunAsync("cat", [], timeout: TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StdOut);
    }

    [Fact]
    public async Task A_child_that_never_finishes_is_stopped_with_PROCESS_TIMEOUT()
    {
        var ex = await Assert.ThrowsAsync<PolifemoException>(
            () => ProcessRunner.RunAsync("sleep", ["30"], timeout: TimeSpan.FromMilliseconds(300)));

        Assert.Equal("PROCESS_TIMEOUT", ex.Code);
    }

    [Fact]
    public async Task Caller_cancellation_stays_a_cancellation_not_a_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ProcessRunner.RunAsync("sleep", ["30"], ct: cts.Token));
    }

    [Fact]
    public async Task Captures_stdout_stderr_and_exit_code()
    {
        var result = await ProcessRunner.RunAsync("sh", ["-c", "echo out; echo err 1>&2; exit 3"]);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("out", result.StdOut.Trim());
        Assert.Equal("err", result.StdErr.Trim());
    }
}

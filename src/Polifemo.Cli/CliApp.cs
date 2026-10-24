using System.CommandLine;

namespace Polifemo.Cli;

public static class CliApp
{
    /// <summary>Builds and runs the command tree. Parse errors exit 2 and write nothing to stdout.</summary>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        Func<CancellationToken, Task<string>>? binaryVersion = null,
        CancellationToken ct = default)
    {
        var root = new RootCommand("Polifemo: platform verification and fixture tooling.");
        root.Subcommands.Add(PlatformCommands.Create(stdout, stderr, binaryVersion));
        root.Subcommands.Add(FixtureCommands.Create(stdout, stderr));

        var parse = root.Parse(args);
        if (parse.Errors.Count > 0)
        {
            foreach (var error in parse.Errors) stderr.WriteLine(error.Message);
            return ExitCodes.InvalidArguments;
        }

        var exit = await parse.InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr }, ct);
        return ct.IsCancellationRequested ? ExitCodes.Cancelled : exit;
    }
}

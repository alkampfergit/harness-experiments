using System.Text.Json;
using Polifemo.Cli;
using Polifemo.Platform.TokenSave;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Cli;

public class PlatformCommandTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private static async Task<(int Exit, string Out, string Err)> Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await CliApp.RunAsync(args, stdout, stderr, binaryVersion: _ => Task.FromResult(PlatformPins.TokenSave));
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task Check_success_prints_the_contract_shape_and_exits_0()
    {
        new SyntheticTokenSave().Build(_ws.Path);

        var (exit, stdout, _) = await Run("platform", "check", "--worktree", _ws.Path);

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        foreach (var p in new[] { "tokensaveBinaryVersion", "indexVersion", "databasePath", "resolution", "mismatches", "warnings" })
            Assert.True(root.TryGetProperty(p, out _), p);
    }

    [Fact]
    public async Task Check_mismatch_exits_1_with_named_code()
    {
        new SyntheticTokenSave().DropTable("nodes").Build(_ws.Path);

        var (exit, stdout, _) = await Run("platform", "check", "--worktree", _ws.Path);

        Assert.Equal(1, exit);
        var m = JsonDocument.Parse(stdout).RootElement.GetProperty("mismatches")[0];
        Assert.Equal("TS_TABLE_MISSING", m.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Expect_tokensave_flag_overrides_the_pin()
    {
        new SyntheticTokenSave().Build(_ws.Path);
        var (exit, stdout, _) = await Run("platform", "check", "--worktree", _ws.Path, "--expect-tokensave", "0.0.0");
        Assert.Equal(1, exit);
        Assert.Contains("TS_VERSION_MISMATCH", stdout);
    }

    [Fact]
    public async Task Missing_required_argument_exits_2()
    {
        var (exit, _, _) = await Run("platform", "check");
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Unreachable_store_exits_3()
    {
        var url = $"http://127.0.0.1:{FakeElasticsearch.FreePort()}";
        var (exit, _, stderr) = await Run("platform", "smoke", "--url", url);
        Assert.Equal(3, exit);
        Assert.Contains("ES_UNREACHABLE", stderr);
    }

    [Fact]
    public async Task Smoke_never_accepts_a_password_argument()
    {
        var (exit, _, _) = await Run("platform", "smoke", "--password", "x");
        Assert.Equal(2, exit);
    }
}

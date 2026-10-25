using System.Text.Json;
using Polifemo.Cli;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Cli;

public class SmokeCommandTests
{
    private static (int, string) Healthy(System.Net.HttpListenerRequest r, string license)
    {
        var path = r.Url!.AbsolutePath;
        if (path == "/") return (200, $"{{\"version\":{{\"number\":\"{PlatformPins.Elasticsearch}\"}}}}");
        if (path == "/_license") return (200, $"{{\"license\":{{\"type\":\"{license}\"}}}}");
        if (path == "/_bulk") return (200, "{\"errors\":false,\"items\":[]}");
        if (path.EndsWith("/_search", StringComparison.Ordinal))
        {
            using var body = new StreamReader(r.InputStream);
            return (200, $"{{\"hits\":{{\"hits\":[{{\"_id\":\"{(body.ReadToEnd().Contains("knn") ? "2" : "3")}\"}}]}}}}");
        }
        return (200, "{\"acknowledged\":true}");
    }

    private static async Task<(int Exit, string Out)> Smoke(string url)
    {
        var stdout = new StringWriter();
        var exit = await CliApp.RunAsync(["platform", "smoke", "--url", url], stdout, new StringWriter(), ct: TestContext.Current.CancellationToken);
        return (exit, stdout.ToString());
    }

    [Fact]
    public async Task Healthy_store_prints_every_check_and_exits_0()
    {
        using var fake = new FakeElasticsearch(r => Healthy(r, "basic"));

        var (exit, output) = await Smoke(fake.Url);

        Assert.Equal(0, exit);
        var root = JsonDocument.Parse(output).RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("basic", root.GetProperty("license").GetString());
        Assert.Equal(["version", "license", "bm25", "knn", "cleanup"],
            root.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_failed_check_exits_1_with_ok_false()
    {
        using var fake = new FakeElasticsearch(r => Healthy(r, "trial"));

        var (exit, output) = await Smoke(fake.Url);

        Assert.Equal(1, exit);
        Assert.False(JsonDocument.Parse(output).RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Check_on_a_missing_worktree_exits_2()
    {
        var stderr = new StringWriter();
        var exit = await CliApp.RunAsync(["platform", "check", "--worktree", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))],
            new StringWriter(), stderr, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains("does not exist", stderr.ToString());
    }
}

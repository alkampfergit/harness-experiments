using System.Net;
using System.Text;
using System.Text.Json;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.Elasticsearch;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Elasticsearch;

/// <summary>Drives the whole smoke flow against an in-process stand-in for the store, so it runs without Docker.</summary>
public class SmokeFlowTests
{
    private const string Hit = "{{\"hits\":{{\"hits\":[{{\"_id\":\"{0}\"}}]}}}}";

    private sealed class Scenario
    {
        public string Bm25Hit { get; init; } = "3";
        public string? KnnHit { get; init; } = "2";
        public string BulkBody { get; init; } = "{\"errors\":false,\"items\":[{\"index\":{\"status\":201}}]}";
        public int DeleteStatus { get; init; } = 200;
        public Func<string, (int, string)?>? Override { get; init; }
        public List<string> Calls { get; } = [];
        public List<string> Auth { get; } = [];
        public string? BulkRequest { get; private set; }
        public string? KnnRequest { get; private set; }

        public (int, string) Handle(HttpListenerRequest r)
        {
            var path = r.Url!.AbsolutePath;
            var slash = path.IndexOf('/', 1);
            Calls.Add($"{r.HttpMethod} {(path.StartsWith("/smoke-", StringComparison.Ordinal) ? "/<index>" + (slash < 0 ? "" : path[slash..]) : path)}");
            Auth.Add(r.Headers["Authorization"] ?? "");
            var body = r.HasEntityBody ? new StreamReader(r.InputStream).ReadToEnd() : "";
            if (Override?.Invoke(path) is { } forced) return forced;
            if (path == "/") return (200, $"{{\"version\":{{\"number\":\"{PlatformPins.Elasticsearch}\"}}}}");
            if (path == "/_license") return (200, "{\"license\":{\"type\":\"basic\"}}");
            if (path == "/_bulk") { BulkRequest = body; return (200, BulkBody); }
            if (path.EndsWith("/_search", StringComparison.Ordinal))
            {
                if (body.Contains("knn", StringComparison.Ordinal))
                {
                    KnnRequest = body;
                    return (200, KnnHit is null ? "{\"hits\":{\"hits\":[]}}" : string.Format(Hit, KnnHit));
                }
                return (200, string.Format(Hit, Bm25Hit));
            }
            return r.HttpMethod == "DELETE" ? (DeleteStatus, "{\"acknowledged\":true}") : (200, "{\"acknowledged\":true}");
        }
    }

    private static async Task<SmokeReport> RunAsync(Scenario s, string user = "elastic", string password = "pw")
    {
        using var fake = new FakeElasticsearch(s.Handle);
        return await SmokeTest.RunAsync(new SmokeOptions(fake.Url, user, password), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Healthy_store_passes_every_check_in_order_and_creates_then_deletes_one_index()
    {
        var s = new Scenario();

        var report = await RunAsync(s);

        Assert.True(report.Ok);
        Assert.Equal(PlatformPins.Elasticsearch, report.ServerVersion);
        Assert.Equal("basic", report.License);
        Assert.Equal(["version", "license", "bm25", "knn", "cleanup"], report.Checks.Select(c => c.Name));
        Assert.Equal(
            ["GET /", "GET /_license", "PUT /<index>", "POST /_bulk", "POST /<index>/_search", "POST /<index>/_search", "DELETE /<index>"],
            s.Calls);
    }

    [Fact]
    public async Task Bulk_payload_is_ndjson_with_five_documents_and_the_knn_query_targets_the_vec_field()
    {
        var s = new Scenario();
        await RunAsync(s);

        var lines = s.BulkRequest!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(10, lines.Length);
        Assert.All(lines, l => JsonDocument.Parse(l).Dispose());
        Assert.Contains("cherry", s.BulkRequest);
        using var knn = JsonDocument.Parse(s.KnnRequest!);
        Assert.Equal("vec", knn.RootElement.GetProperty("knn").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Basic_authentication_header_is_sent_only_when_both_credentials_are_set()
    {
        var withAuth = new Scenario();
        await RunAsync(withAuth, "elastic", "pw");
        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("elastic:pw"));
        Assert.All(withAuth.Auth, a => Assert.Equal(expected, a));

        var anonymous = new Scenario();
        await RunAsync(anonymous, "", "");
        Assert.All(anonymous.Auth, a => Assert.Equal("", a));
    }

    [Fact]
    public async Task Wrong_top_hit_fails_that_check_and_names_expected_and_found()
    {
        var report = await RunAsync(new Scenario { Bm25Hit = "1" });

        Assert.False(report.Ok);
        var bm25 = Assert.Single(report.Checks, c => c.Name == "bm25");
        Assert.False(bm25.Ok);
        Assert.Contains("expected top hit 3", bm25.Detail);
        Assert.Contains("found 1", bm25.Detail);
        Assert.True(report.Checks.Single(c => c.Name == "knn").Ok);
    }

    [Fact]
    public async Task Knn_without_hits_reports_none_found()
    {
        var report = await RunAsync(new Scenario { KnnHit = null });

        var knn = Assert.Single(report.Checks, c => c.Name == "knn");
        Assert.False(knn.Ok);
        Assert.Contains("found none", knn.Detail);
    }

    [Fact]
    public async Task Failed_bulk_items_are_ES_BULK_FAILED_and_the_index_is_still_cleaned_up()
    {
        var s = new Scenario { BulkBody = "{\"errors\":true,\"items\":[{\"index\":{\"status\":400}},{\"index\":{\"status\":201}}]}" };

        var ex = await Assert.ThrowsAsync<SearchStoreException>(() => RunAsync(s));

        Assert.Equal("ES_BULK_FAILED", ex.Code);
        Assert.Contains("1 of 5", ex.Message);
        Assert.Equal("DELETE /<index>", s.Calls[^1]);
    }

    [Fact]
    public async Task Cleanup_failure_is_reported_as_a_failed_check_not_an_exception()
    {
        var report = await RunAsync(new Scenario { DeleteStatus = 500 });

        Assert.False(report.Ok);
        var cleanup = Assert.Single(report.Checks, c => c.Name == "cleanup");
        Assert.False(cleanup.Ok);
        Assert.Contains("500", cleanup.Detail);
        Assert.True(report.Checks.Where(c => c.Name != "cleanup").All(c => c.Ok));
    }

    [Fact]
    public async Task Server_error_is_ES_REQUEST_FAILED_with_method_path_and_status()
    {
        var ex = await Assert.ThrowsAsync<SearchStoreException>(() =>
            RunAsync(new Scenario { Override = p => p == "/_license" ? (503, "{}") : null }));

        Assert.Equal("ES_REQUEST_FAILED", ex.Code);
        Assert.Contains("GET /_license", ex.Message);
        Assert.Contains("503", ex.Message);
    }

    [Fact]
    public async Task Non_json_answer_is_ES_REQUEST_FAILED_invalid_json()
    {
        var ex = await Assert.ThrowsAsync<SearchStoreException>(() =>
            RunAsync(new Scenario { Override = p => p == "/" ? (200, "<html>proxy</html>") : null }));

        Assert.Equal("ES_REQUEST_FAILED", ex.Code);
        Assert.Contains("invalid JSON", ex.Message);
    }

    [Fact]
    public void Options_trim_the_trailing_slash_and_never_print_the_password()
    {
        var options = new SmokeOptions("http://host:9200/", "elastic", "hunter2");

        Assert.Equal("http://host:9200", options.Url);
        Assert.Equal("elastic@http://host:9200", options.ToString());
        Assert.DoesNotContain("hunter2", options.ToString());
    }
}

[CollectionDefinition("EnvironmentVariables", DisableParallelization = true)]
public sealed class EnvironmentVariablesCollection;

/// <summary>Changes process-wide variables, so it must not run beside any other test class.</summary>
[Collection("EnvironmentVariables")]
public sealed class SmokeOptionsEnvironmentTests : IDisposable
{
    private static readonly string[] Names = ["ELASTICSEARCH_URL", "ELASTICSEARCH_USERNAME", "ELASTICSEARCH_PASSWORD"];
    private readonly Dictionary<string, string?> _saved = Names.ToDictionary(n => n, Environment.GetEnvironmentVariable);

    public SmokeOptionsEnvironmentTests()
    {
        foreach (var n in Names) Environment.SetEnvironmentVariable(n, null);
    }

    public void Dispose()
    {
        foreach (var (n, v) in _saved) Environment.SetEnvironmentVariable(n, v);
    }

    [Fact]
    public void Nothing_set_means_anonymous_access_to_localhost()
    {
        var o = SmokeOptions.FromEnvironment(null, null);

        Assert.Equal("http://localhost:9200", o.Url);
        Assert.Equal("", o.Username);
    }

    [Fact]
    public void A_password_alone_defaults_the_user_to_elastic()
    {
        Environment.SetEnvironmentVariable("ELASTICSEARCH_PASSWORD", "pw");

        Assert.Equal("elastic", SmokeOptions.FromEnvironment(null, null).Username);
    }

    [Fact]
    public void A_username_without_a_password_is_ES_CREDENTIALS_INCOMPLETE()
    {
        var ex = Assert.Throws<SearchStoreException>(() => SmokeOptions.FromEnvironment(null, "bob"));
        Assert.Equal("ES_CREDENTIALS_INCOMPLETE", ex.Code);
    }

    [Fact]
    public void Arguments_win_over_the_environment()
    {
        Environment.SetEnvironmentVariable("ELASTICSEARCH_URL", "http://env:9200");
        Environment.SetEnvironmentVariable("ELASTICSEARCH_USERNAME", "env-user");
        Environment.SetEnvironmentVariable("ELASTICSEARCH_PASSWORD", "pw");

        var fromEnv = SmokeOptions.FromEnvironment(null, null);
        var fromArgs = SmokeOptions.FromEnvironment("http://arg:9200", "arg-user");

        Assert.Equal(("http://env:9200", "env-user"), (fromEnv.Url, fromEnv.Username));
        Assert.Equal(("http://arg:9200", "arg-user"), (fromArgs.Url, fromArgs.Username));
    }
}

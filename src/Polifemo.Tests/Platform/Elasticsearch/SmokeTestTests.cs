using Polifemo.Contracts.Errors;
using Polifemo.Platform.Elasticsearch;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Elasticsearch;

public class SmokeTestTests
{
    private const string Secret = "s3cret-pw-do-not-print";

    [Fact]
    public async Task Unreachable_store_is_ES_UNREACHABLE()
    {
        var url = $"http://127.0.0.1:{FakeElasticsearch.FreePort()}";
        var ex = await Assert.ThrowsAsync<SearchStoreException>(() =>
            new SmokeTest().RunAsync(new SmokeOptions(url, "elastic", Secret), ct: TestContext.Current.CancellationToken));
        Assert.Equal("ES_UNREACHABLE", ex.Code);
        Assert.DoesNotContain(Secret, ex.ToString());
    }

    [Fact]
    public async Task Rejected_credentials_are_ES_AUTH_FAILED_and_never_print_the_password()
    {
        using var fake = new FakeElasticsearch(_ => (401, "{\"error\":\"unauthorized\"}"));
        var ex = await Assert.ThrowsAsync<SearchStoreException>(() =>
            new SmokeTest().RunAsync(new SmokeOptions(fake.Url, "elastic", Secret), ct: TestContext.Current.CancellationToken));

        Assert.Equal("ES_AUTH_FAILED", ex.Code);
        Assert.Contains(fake.Url, ex.Message);
        Assert.Contains("elastic", ex.Message);
        Assert.DoesNotContain(Secret, ex.ToString());
    }

    [Theory]
    [InlineData("8.15.0")]
    [InlineData("7.17.3")]
    [InlineData("9.4.0")]
    public async Task Other_versions_are_ES_VERSION_UNSUPPORTED_and_named(string version)
    {
        using var fake = new FakeElasticsearch(_ => (200, $"{{\"version\":{{\"number\":\"{version}\"}}}}"));
        var ex = await Assert.ThrowsAsync<SearchStoreException>(() =>
            new SmokeTest().RunAsync(new SmokeOptions(fake.Url, "elastic", Secret), ct: TestContext.Current.CancellationToken));

        Assert.Equal("ES_VERSION_UNSUPPORTED", ex.Code);
        Assert.Contains(version, ex.Message);
    }

    [Fact]
    public async Task Non_basic_license_fails_the_license_check()
    {
        using var fake = new FakeElasticsearch(r => r.Url!.AbsolutePath == "/_license"
            ? (200, "{\"license\":{\"type\":\"trial\"}}")
            : (200, $"{{\"version\":{{\"number\":\"{PlatformPins.Elasticsearch}\"}}}}"));

        var report = await new SmokeTest().RunAsync(new SmokeOptions(fake.Url, "elastic", Secret), ct: TestContext.Current.CancellationToken);

        Assert.False(report.Ok);
        Assert.Contains(report.Checks, c => c.Name == "license" && !c.Ok);
    }

    [Fact]
    public void Url_with_embedded_credentials_is_rejected_without_echoing_the_secret()
    {
        var ex = Assert.Throws<SearchStoreException>(() => SmokeOptions.FromEnvironment("http://elastic:s3cret@localhost:9200", null));

        Assert.Equal("ES_URL_HAS_CREDENTIALS", ex.Code);
        Assert.DoesNotContain("s3cret", ex.Message);
    }

    [Fact]
    public async Task Real_store_passes_bm25_and_knn_and_cleans_up()
    {
        var url = Environment.GetEnvironmentVariable("ELASTICSEARCH_URL");
        if (string.IsNullOrEmpty(url)) { Assert.Skip("ELASTICSEARCH_URL is not set."); return; }
        var options = SmokeOptions.FromEnvironment(url, null);

        var report = await new SmokeTest().RunAsync(options, ct: TestContext.Current.CancellationToken);

        Assert.True(report.Ok, string.Join("; ", report.Checks.Select(c => $"{c.Name}={c.Ok} {c.Detail}")));
        Assert.Equal(PlatformPins.Elasticsearch, report.ServerVersion);
        Assert.Equal("basic", report.License);
        Assert.Equal(["version", "license", "bm25", "knn", "cleanup"], report.Checks.Select(c => c.Name));
        Assert.All(report.Checks, c => Assert.True(c.Ok));
    }

    [Fact]
    public async Task Real_store_with_wrong_password_is_ES_AUTH_FAILED()
    {
        var url = Environment.GetEnvironmentVariable("ELASTICSEARCH_URL");
        if (string.IsNullOrEmpty(url)) { Assert.Skip("ELASTICSEARCH_URL is not set."); return; }
        using (var anonymous = new HttpClient())
        {
            var status = (await anonymous.GetAsync(url, cancellationToken: TestContext.Current.CancellationToken)).StatusCode;
            if (status == System.Net.HttpStatusCode.OK)
            {
                Assert.Skip("The store at ELASTICSEARCH_URL does not enforce authentication (anonymous GET / succeeded).");
                return;
            }
        }

        var ex = await Assert.ThrowsAsync<SearchStoreException>(() =>
            new SmokeTest().RunAsync(new SmokeOptions(url, "elastic", Secret), ct: TestContext.Current.CancellationToken));

        Assert.Equal("ES_AUTH_FAILED", ex.Code);
        Assert.DoesNotContain(Secret, ex.ToString());
    }
}

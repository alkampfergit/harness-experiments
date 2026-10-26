using System.Net;
using System.Text.RegularExpressions;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.Privacy;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Privacy;

/// <summary>No payload reaches the network unless the destination is local, on every path that carries content.</summary>
public sealed partial class EgressTests
{
    private const string Payload = "SECRET-SOURCE-AND-QUERY-TEXT";

    private sealed class Recorder : HttpMessageHandler
    {
        public List<Uri> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    // Each path lists the kinds of service it sends content to: indexing stores documents and embeds them,
    // search embeds the query and queries the store, reranking sends candidates to the reranker.
    public static TheoryData<string, EndpointKind[]> Paths => new()
    {
        { "indexing", [EndpointKind.Embedding, EndpointKind.Store] },
        { "search", [EndpointKind.Embedding, EndpointKind.Store] },
        { "reranking", [EndpointKind.Reranking] },
    };

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task A_payload_to_an_external_host_never_reaches_the_transport(string path, EndpointKind[] kinds)
    {
        foreach (var kind in kinds)
        {
            var recorder = new Recorder();
            using var client = LocalOnlyHttp.CreateClient(new LocalOnlyPolicy(), kind, recorder);

            await Assert.ThrowsAsync<LocalOnlyPolicyException>(() =>
                client.PostAsync("https://hosted.example.com/v1/embed", new StringContent(Payload), TestContext.Current.CancellationToken));

            Assert.True(recorder.Seen.Count == 0, $"{path}/{kind}: a request left the guard");
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task A_payload_to_a_local_service_is_sent(string path, EndpointKind[] kinds)
    {
        foreach (var kind in kinds)
        {
            var recorder = new Recorder();
            using var client = LocalOnlyHttp.CreateClient(new LocalOnlyPolicy(), kind, recorder);

            await client.PostAsync("http://127.0.0.1:8081/v1/embed", new StringContent(Payload), TestContext.Current.CancellationToken);

            Assert.True(recorder.Seen.Count == 1, $"{path}/{kind}: the local request was blocked");
        }
    }

    [Fact]
    public void The_transport_ignores_the_system_proxy_and_does_not_follow_redirects()
    {
        using var transport = LocalOnlyHttp.CreateTransport();

        Assert.False(transport.UseProxy);
        Assert.False(transport.AllowAutoRedirect);
    }

    [Fact]
    public async Task An_external_redirect_from_a_local_service_is_returned_not_followed()
    {
        var hits = 0;
        using var fake = new FakeElasticsearch(_ => { hits++; return (302, ""); });
        using var client = LocalOnlyHttp.CreateClient(new LocalOnlyPolicy(), EndpointKind.Store);

        var response = await client.GetAsync(fake.Url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(1, hits);
    }

    [GeneratedRegex(@"\bnew\s+HttpClient\b|\bHttpClientHandler\b|\bSocketsHttpHandler\b|\bWebClient\b|\bTcpClient\b|\bWebRequest\b|\bClientWebSocket\b")]
    private static partial Regex RawNetworking();

    [Fact]
    public void Product_code_opens_connections_only_through_the_guarded_client()
    {
        var src = Path.Combine(TestWorkspace.RepoRoot(), "src");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("/fixtures/")
                        && !f.Contains("/Polifemo.Tests/") && !f.Contains("/Privacy/"))
            .Where(f => RawNetworking().IsMatch(File.ReadAllText(f)))
            .ToList();

        Assert.True(offenders.Count == 0, "Use LocalOnlyHttp.CreateClient instead of raw networking in: " + string.Join(", ", offenders));
    }

    [Fact]
    public async Task Smoke_against_an_external_store_is_refused_before_any_request()
    {
        var ex = await Assert.ThrowsAsync<LocalOnlyPolicyException>(() =>
            Polifemo.Platform.Elasticsearch.SmokeTest.RunAsync(new Polifemo.Platform.Elasticsearch.SmokeOptions("https://hosted.example.com:9200", "elastic", "pw"), TestContext.Current.CancellationToken));

        Assert.Equal("LOCAL_ONLY_EXTERNAL_ENDPOINT", ex.Code);
    }
}

using System.Net;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.Elasticsearch;
using Polifemo.Platform.Privacy;

namespace Polifemo.Tests.Platform.Privacy;

/// <summary>F02-01: hosted endpoints are refused by the server-side policy, per capability, with no egress.</summary>
public sealed class LocalOnlyPolicyTests
{
    private static readonly LocalEndpointPolicy Policy = LocalEndpointPolicy.Default;

    [Theory]
    [InlineData("http://localhost:8000")]
    [InlineData("http://127.0.0.1:9200")]
    [InlineData("http://127.5.5.5:9200")]
    [InlineData("http://[::1]:8000")]
    [InlineData("http://elasticsearch:9200")]
    [InlineData("https://embed.localhost:8443")]
    public void Local_endpoints_are_accepted(string url) =>
        Assert.Equal(new Uri(url), Policy.Validate(ProcessingCapability.Embedding, url));

    [Theory]
    [InlineData("https://api.openai.com/v1/embeddings")]
    [InlineData("http://10.0.0.5:8000")]
    [InlineData("http://192.168.1.10:9200")]
    [InlineData("http://localhost.evil.example")]
    [InlineData("http://127.0.0.1.evil.example")]
    [InlineData("http://embed.internal.example:8000")]
    [InlineData("ftp://localhost/x")]
    [InlineData("file:///etc/passwd")]
    public void External_endpoints_are_refused_without_naming_the_host(string url)
    {
        var ex = Assert.Throws<PrivacyPolicyException>(() => Policy.Validate(ProcessingCapability.Reranking, url));

        Assert.StartsWith("POLICY_", ex.Code);
        Assert.Contains("Reranking", ex.Message);
        var host = new Uri(url).Host;
        Assert.True(host.Length == 0 || !ex.Message.Contains(host, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Credentials_in_a_url_are_refused()
    {
        var ex = Assert.Throws<PrivacyPolicyException>(() => Policy.Validate(ProcessingCapability.Store, "http://elastic:hunter2@localhost:9200"));

        Assert.Equal("POLICY_URL_HAS_CREDENTIALS", ex.Code);
        Assert.DoesNotContain("hunter2", ex.Message);
    }

    [Fact]
    public void Not_a_url_is_refused() =>
        Assert.Equal("POLICY_ENDPOINT_INVALID", Assert.Throws<PrivacyPolicyException>(() => Policy.Validate(ProcessingCapability.Store, "nonsense")).Code);

    [Theory]
    [InlineData("example.com")]
    [InlineData("10.0.0.1")]
    [InlineData("")]
    public void Approved_names_must_be_single_label_service_names(string name) =>
        Assert.Throws<ArgumentException>(() => new LocalEndpointPolicy([name]));

    [Fact]
    public void Defaults_and_overrides_are_resolved_per_capability()
    {
        var resolved = ProcessingPolicy.Resolve(Policy,
            new ProcessingSettings("http://localhost:8001", null, "http://localhost:8002"),
            new ProcessingSettings(RerankingUrl: "http://127.0.0.1:8003"));

        Assert.Equal(new Uri("http://localhost:8001"), resolved.Embedding);
        Assert.Equal(new Uri("http://127.0.0.1:8003"), resolved.Reranking);
        Assert.Equal(new Uri("http://localhost:8002"), resolved.Query);
    }

    public static TheoryData<string> Capabilities => ["embedding", "reranking", "query"];

    [Theory]
    [MemberData(nameof(Capabilities))]
    public void Each_capability_rejects_a_hosted_caller_override_on_its_own(string capability)
    {
        var local = new ProcessingSettings("http://localhost:1", "http://localhost:2", "http://localhost:3");
        const string hosted = "https://hosted.example.com";
        var attempt = capability switch
        {
            "embedding" => new ProcessingSettings(EmbeddingUrl: hosted),
            "reranking" => new ProcessingSettings(RerankingUrl: hosted),
            _ => new ProcessingSettings(QueryUrl: hosted),
        };

        var ex = Assert.Throws<PrivacyPolicyException>(() => ProcessingPolicy.Resolve(Policy, local, attempt));

        Assert.Equal("POLICY_EXTERNAL_ENDPOINT", ex.Code);
        Assert.Contains("caller override", ex.Message);
    }

    [Fact]
    public void A_hosted_profile_default_is_rejected_too() =>
        Assert.Throws<PrivacyPolicyException>(() => ProcessingPolicy.Resolve(Policy, new ProcessingSettings(EmbeddingUrl: "https://hosted.example.com")));

    [Fact]
    public void Telemetry_exporters_must_stay_local()
    {
        TelemetryPolicy.Validate(Policy, _ => null);
        TelemetryPolicy.Validate(Policy, v => v == "OTEL_EXPORTER_OTLP_ENDPOINT" ? "http://localhost:4317" : null);

        foreach (var variable in TelemetryPolicy.EndpointVariables)
        {
            var ex = Assert.Throws<PrivacyPolicyException>(
                () => TelemetryPolicy.Validate(Policy, v => v == variable ? "https://otel.vendor.example" : null));
            Assert.Contains(variable, ex.Message);
        }
    }
}

/// <summary>F02-01: payload-bearing requests of the indexing, search and reranking paths never leave the machine.</summary>
public sealed class EgressTests
{
    private sealed class Recorder : HttpMessageHandler
    {
        public List<Uri> Sent { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    public static TheoryData<ProcessingCapability, string> Paths => new()
    {
        { ProcessingCapability.Store, "https://es.hosted.example/_bulk" },
        { ProcessingCapability.Embedding, "https://embed.hosted.example/embed" },
        { ProcessingCapability.QueryTransmission, "https://search.hosted.example/query?q=secret+question" },
        { ProcessingCapability.Reranking, "https://rerank.hosted.example/rerank" },
    };

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task A_hosted_request_is_refused_before_anything_is_sent(ProcessingCapability capability, string url)
    {
        var inner = new Recorder();
        using var http = LocalOnlyHttp.CreateClient(LocalEndpointPolicy.Default, capability, inner);

        var ex = await Assert.ThrowsAsync<PrivacyPolicyException>(
            () => http.PostAsync(url, new StringContent("source text"), TestContext.Current.CancellationToken));

        Assert.Equal("POLICY_EXTERNAL_ENDPOINT", ex.Code);
        Assert.Empty(inner.Sent);
    }

    [Theory]
    [InlineData(ProcessingCapability.Store, "http://elasticsearch:9200/_bulk")]
    [InlineData(ProcessingCapability.Embedding, "http://127.0.0.1:8000/embed")]
    [InlineData(ProcessingCapability.Reranking, "http://localhost:8001/rerank")]
    public async Task A_local_request_goes_through(ProcessingCapability capability, string url)
    {
        var inner = new Recorder();
        using var http = LocalOnlyHttp.CreateClient(LocalEndpointPolicy.Default, capability, inner);

        await http.PostAsync(url, new StringContent("x"), TestContext.Current.CancellationToken);

        Assert.Equal([new Uri(url)], inner.Sent);
    }

    [Fact]
    public async Task The_smoke_test_refuses_a_hosted_store_before_any_request()
    {
        var inner = new Recorder();
        var options = new SmokeOptions("https://es.hosted.example:9200", "elastic", "pw");

        await Assert.ThrowsAsync<PrivacyPolicyException>(
            () => SmokeTest.RunAsync(options, inner, TestContext.Current.CancellationToken));

        Assert.Empty(inner.Sent);
    }

    [Fact]
    public void The_default_client_follows_no_redirects_and_uses_no_proxy()
    {
        using var http = LocalOnlyHttp.CreateClient(LocalEndpointPolicy.Default, ProcessingCapability.Store);

        Assert.NotNull(http);
    }
}

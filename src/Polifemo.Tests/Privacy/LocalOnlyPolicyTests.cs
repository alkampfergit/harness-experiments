using Polifemo.Contracts.Errors;
using Polifemo.Platform.Privacy;

namespace Polifemo.Tests.Privacy;

public class LocalOnlyPolicyTests
{
    private static readonly LocalOnlyPolicy Policy = new();

    [Theory]
    [InlineData("http://localhost:9200")]
    [InlineData("http://127.0.0.1:8081/v1/embeddings")]
    [InlineData("http://[::1]:8082")]
    [InlineData("http://elasticsearch:9200")]
    public void Accepts_local_endpoints(string url) =>
        Assert.Equal(new Uri(url), Policy.Validate(EndpointKind.Embedding, url, "test"));

    [Theory]
    [InlineData("https://api.openai.com/v1/embeddings")]
    [InlineData("https://api.voyageai.com")]
    [InlineData("http://localhost.evil.com:9200")]
    [InlineData("http://10.0.0.5:8081")]
    [InlineData("http://169.254.169.254")]
    [InlineData("http://embeddings.internal.example")]
    public void Rejects_external_endpoints_with_an_explicit_diagnostic(string url)
    {
        var ex = Assert.Throws<LocalOnlyPolicyException>(() => Policy.Validate(EndpointKind.Reranking, url, "POLIFEMO_RERANKING_URL"));

        Assert.Equal("LOCAL_ONLY_EXTERNAL_ENDPOINT", ex.Code);
        Assert.Contains("Reranking", ex.Message);
        Assert.Contains("POLIFEMO_RERANKING_URL", ex.Message);
        Assert.Contains("not local", ex.Message);
    }

    [Fact]
    public void Diagnostics_never_echo_credentials_paths_or_queries()
    {
        var external = Assert.Throws<LocalOnlyPolicyException>(() =>
            Policy.Validate(EndpointKind.Embedding, "https://api.example.com/secret-path?key=TOPSECRET", "test"));
        var credentials = Assert.Throws<LocalOnlyPolicyException>(() =>
            Policy.Validate(EndpointKind.Store, "http://elastic:TOPSECRET@localhost:9200", "test"));

        Assert.DoesNotContain("TOPSECRET", external.Message + credentials.Message);
        Assert.DoesNotContain("secret-path", external.Message);
        Assert.Equal("LOCAL_ONLY_CREDENTIALS_IN_URL", credentials.Code);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://localhost/file")]
    public void Rejects_values_that_are_not_http_urls(string url) =>
        Assert.Equal("LOCAL_ONLY_INVALID_ENDPOINT", Assert.Throws<LocalOnlyPolicyException>(() => Policy.Validate(EndpointKind.Store, url, "t")).Code);

    [Theory]
    [InlineData("example.com")]
    [InlineData("10.0.0.1")]
    [InlineData("a:1")]
    public void The_service_allowlist_accepts_only_single_label_names(string host) =>
        Assert.Equal("LOCAL_ONLY_BAD_ALLOWLIST", Assert.Throws<LocalOnlyPolicyException>(() => new LocalOnlyPolicy([host])).Code);

    [Theory]
    [InlineData(EndpointKind.Store)]
    [InlineData(EndpointKind.Embedding)]
    [InlineData(EndpointKind.Reranking)]
    public void A_caller_override_to_an_external_host_is_rejected_for_every_kind(EndpointKind kind)
    {
        var ex = Assert.Throws<LocalOnlyPolicyException>(() => Policy.Resolve(kind, "http://127.0.0.1:8081", "https://hosted.example.com"));

        Assert.Equal("LOCAL_ONLY_EXTERNAL_ENDPOINT", ex.Code);
        Assert.Contains("caller override", ex.Message);
    }

    [Fact]
    public void A_local_caller_override_is_accepted_and_an_external_configuration_is_not()
    {
        Assert.Equal(new Uri("http://127.0.0.1:9000"), Policy.Resolve(EndpointKind.Embedding, "http://127.0.0.1:8081", "http://127.0.0.1:9000"));
        Assert.Equal(new Uri("http://127.0.0.1:8081"), Policy.Resolve(EndpointKind.Embedding, "http://127.0.0.1:8081", null));
        Assert.Contains("server configuration",
            Assert.Throws<LocalOnlyPolicyException>(() => Policy.Resolve(EndpointKind.Embedding, "https://hosted.example.com", null)).Message);
    }
}

public class LocalOnlySettingsTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] values) =>
        name => values.Where(v => v.Key == name).Select(v => v.Value).FirstOrDefault();

    [Fact]
    public void Defaults_are_local()
    {
        var s = LocalOnlySettings.FromEnvironment(Env());

        Assert.All(new[] { s.Store, s.Embedding, s.Reranking }, u => Assert.True(u.IsLoopback));
    }

    [Theory]
    [InlineData(LocalOnlySettings.EmbeddingUrlVariable)]
    [InlineData(LocalOnlySettings.RerankingUrlVariable)]
    [InlineData(LocalOnlySettings.StoreUrlVariable)]
    public void An_external_endpoint_in_the_environment_names_the_variable(string variable)
    {
        var ex = Assert.Throws<LocalOnlyPolicyException>(() => LocalOnlySettings.FromEnvironment(Env((variable, "https://hosted.example.com"))));

        Assert.Contains(variable, ex.Message);
    }

    [Theory]
    [InlineData("POLIFEMO_ALLOW_HOSTED_EMBEDDINGS")]
    [InlineData("POLIFEMO_ALLOW_HOSTED_RERANKING")]
    [InlineData("POLIFEMO_ALLOW_EXTERNAL_QUERIES")]
    public void Each_hosted_flag_is_denied_independently(string flag)
    {
        var ex = Assert.Throws<LocalOnlyPolicyException>(() => LocalOnlySettings.FromEnvironment(Env((flag, "true"))));

        Assert.Equal("LOCAL_ONLY_HOSTED_DENIED", ex.Code);
        Assert.Contains(flag, ex.Message);
    }

    [Fact]
    public void An_operator_can_allow_a_compose_service_name()
    {
        var s = LocalOnlySettings.FromEnvironment(Env((LocalOnlySettings.ServiceHostsVariable, "elasticsearch,embedder"), (LocalOnlySettings.EmbeddingUrlVariable, "http://embedder:8081")));

        Assert.Equal("embedder", s.Embedding.Host);
    }

    [Fact]
    public void Offline_variables_are_reported_when_missing()
    {
        Assert.Equal(OfflineEnvironment.Variables.Keys.Order(), OfflineEnvironment.Missing(Env()).Order());
        Assert.Empty(OfflineEnvironment.Missing(n => OfflineEnvironment.Variables.GetValueOrDefault(n)));
        Assert.Equal(["HF_HUB_OFFLINE"], OfflineEnvironment.Missing(n => n == "HF_HUB_OFFLINE" ? "0" : OfflineEnvironment.Variables[n]));
    }
}

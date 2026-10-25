using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Polifemo.Contracts.Errors;

namespace Polifemo.Platform.Elasticsearch;

/// <summary>Connection settings. Credentials are optional; the password is never part of <see cref="ToString"/>.</summary>
public sealed class SmokeOptions(string url, string username, string password)
{
    public string Url { get; } = url.TrimEnd('/');
    public string Username { get; } = username;
    internal string Password { get; } = password;

    public static SmokeOptions FromEnvironment(string? url, string? username)
    {
        var password = Environment.GetEnvironmentVariable("ELASTICSEARCH_PASSWORD") ?? "";
        var resolvedUsername = username ?? Environment.GetEnvironmentVariable("ELASTICSEARCH_USERNAME");
        if (string.IsNullOrWhiteSpace(resolvedUsername))
            resolvedUsername = string.IsNullOrEmpty(password) ? "" : "elastic";
        if (!string.IsNullOrEmpty(resolvedUsername) != !string.IsNullOrEmpty(password))
            throw new SearchStoreException("ES_CREDENTIALS_INCOMPLETE",
                "Set both ELASTICSEARCH_USERNAME and ELASTICSEARCH_PASSWORD, or leave both unset for anonymous access.");
        var resolvedUrl = url ?? Environment.GetEnvironmentVariable("ELASTICSEARCH_URL") ?? "http://localhost:9200";
        // Errors echo the URL to both output streams, so a password embedded in it would leak.
        if (Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.UserInfo))
            throw new SearchStoreException("ES_URL_HAS_CREDENTIALS",
                "The Elasticsearch URL must not contain credentials; use ELASTICSEARCH_USERNAME and ELASTICSEARCH_PASSWORD.");
        return new SmokeOptions(resolvedUrl, resolvedUsername ?? "", password);
    }

    public override string ToString() => $"{Username}@{Url}";
}

public sealed record SmokeCheck(string Name, bool Ok, string? Detail = null);

public sealed record SmokeReport(string ServerVersion, string? License, IReadOnlyList<SmokeCheck> Checks)
{
    public bool Ok => Checks.All(c => c.Ok);
}

/// <summary>
/// Exercises BM25 and top-level <c>knn</c> on synthetic documents (research R4), with optional Basic authentication.
/// It uses no licensed retriever (RRF, text_similarity_reranker) and no inference endpoint.
/// </summary>
public sealed class SmokeTest
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly (string Id, string Text, float[] Vec)[] Docs =
    [
        ("1", "alpha apple pie", [1f, 0f, 0f, 0f]),
        ("2", "bravo banana split", [0f, 1f, 0f, 0f]),
        ("3", "charlie cherry tart", [0f, 0f, 1f, 0f]),
        ("4", "delta date loaf", [0f, 0f, 0f, 1f]),
        ("5", "echo elderberry jam", [0.7f, 0.7f, 0f, 0f]),
    ];

    public async Task<SmokeReport> RunAsync(SmokeOptions options, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrEmpty(options.Username) && !string.IsNullOrEmpty(options.Password))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}")));

        var checks = new List<SmokeCheck>();

        var root = await SendAsync(http, options, HttpMethod.Get, "/", null, ct);
        var version = root.GetProperty("version").GetProperty("number").GetString() ?? "";
        if (version != PlatformPins.Elasticsearch)
            throw new SearchStoreException("ES_VERSION_UNSUPPORTED",
                $"Elasticsearch {version} at {options.Url} is not supported; the pinned version is {PlatformPins.Elasticsearch}.");
        checks.Add(new SmokeCheck("version", true, version));

        var licenseDoc = await SendAsync(http, options, HttpMethod.Get, "/_license", null, ct);
        var license = licenseDoc.GetProperty("license").GetProperty("type").GetString();
        var licenseOk = license == "basic";
        checks.Add(new SmokeCheck("license", licenseOk, licenseOk ? license : $"expected basic, found {license}"));
        if (!licenseOk) return new SmokeReport(version, license, checks);

        var index = $"smoke-{Guid.NewGuid():N}";
        try
        {
            await CreateIndexAsync(http, options, index, ct);
            await BulkAsync(http, options, index, ct);
            checks.Add(await Bm25Async(http, options, index, ct));
            checks.Add(await KnnAsync(http, options, index, ct));
        }
        finally
        {
            checks.Add(await CleanupAsync(http, options, index));
        }

        return new SmokeReport(version, license, checks);
    }

    private static Task CreateIndexAsync(HttpClient http, SmokeOptions o, string index, CancellationToken ct) =>
        SendAsync(http, o, HttpMethod.Put, $"/{index}", new
        {
            settings = new { number_of_shards = 1, number_of_replicas = 0 },
            mappings = new
            {
                properties = new
                {
                    text = new { type = "text" },
                    vec = new { type = "dense_vector", dims = 4, index = true, similarity = "cosine" },
                },
            },
        }, ct);

    private static async Task BulkAsync(HttpClient http, SmokeOptions o, string index, CancellationToken ct)
    {
        var body = new StringBuilder();
        foreach (var (id, text, vec) in Docs)
        {
            body.Append(JsonSerializer.Serialize(new { index = new { _index = index, _id = id } }, Json)).Append('\n');
            body.Append(JsonSerializer.Serialize(new { text, vec }, Json)).Append('\n');
        }

        var result = await SendAsync(http, o, HttpMethod.Post, "/_bulk?refresh=wait_for", body.ToString(), ct);
        var failed = result.GetProperty("items").EnumerateArray()
            .Count(i => i.GetProperty("index").GetProperty("status").GetInt32() is < 200 or >= 300);
        if (result.GetProperty("errors").GetBoolean() || failed > 0)
            throw new SearchStoreException("ES_BULK_FAILED", $"{failed} of {Docs.Length} bulk items failed on {o.Url}.");
    }

    private static async Task<SmokeCheck> Bm25Async(HttpClient http, SmokeOptions o, string index, CancellationToken ct)
    {
        var r = await SendAsync(http, o, HttpMethod.Post, $"/{index}/_search",
            new { size = 1, query = new { match = new { text = "cherry" } } }, ct);
        return Assert("bm25", TopHitId(r), "3");
    }

    private static async Task<SmokeCheck> KnnAsync(HttpClient http, SmokeOptions o, string index, CancellationToken ct)
    {
        var r = await SendAsync(http, o, HttpMethod.Post, $"/{index}/_search", new
        {
            size = 1,
            knn = new { field = "vec", query_vector = new[] { 0.05f, 0.95f, 0.1f, 0f }, k = 1, num_candidates = 5 },
        }, ct);
        return Assert("knn", TopHitId(r), "2");
    }

    private static async Task<SmokeCheck> CleanupAsync(HttpClient http, SmokeOptions o, string index)
    {
        try
        {
            await SendAsync(http, o, HttpMethod.Delete, $"/{index}", null, CancellationToken.None);
            return new SmokeCheck("cleanup", true, index);
        }
        catch (SearchStoreException ex)
        {
            return new SmokeCheck("cleanup", false, ex.Message);
        }
    }

    private static string? TopHitId(JsonElement result)
    {
        var hits = result.GetProperty("hits").GetProperty("hits");
        return hits.GetArrayLength() > 0 ? hits[0].GetProperty("_id").GetString() : null;
    }

    private static SmokeCheck Assert(string name, string? actual, string expected) =>
        actual == expected
            ? new SmokeCheck(name, true, $"top hit {actual}")
            : new SmokeCheck(name, false, $"expected top hit {expected}, found {actual ?? "none"}");

    private static async Task<JsonElement> SendAsync(
        HttpClient http, SmokeOptions o, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, o.Url + path);
        if (body is string ndjson)
            request.Content = new StringContent(ndjson, Encoding.UTF8, "application/x-ndjson");
        else if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new SearchStoreException("ES_UNREACHABLE", $"Cannot reach Elasticsearch at {o.Url}.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new SearchStoreException("ES_UNREACHABLE", $"Elasticsearch at {o.Url} did not answer in time.", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new SearchStoreException("ES_AUTH_FAILED",
                    $"Elasticsearch at {o.Url} rejected the credentials for user '{o.Username}'.");

            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new SearchStoreException("ES_REQUEST_FAILED",
                    $"{method} {path} on {o.Url} returned {(int)response.StatusCode}.");

            try
            {
                return JsonDocument.Parse(text).RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new SearchStoreException("ES_REQUEST_FAILED", $"{method} {path} on {o.Url} returned invalid JSON.", ex);
            }
        }
    }
}

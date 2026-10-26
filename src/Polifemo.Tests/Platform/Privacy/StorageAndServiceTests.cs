using System.Net;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.Privacy;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.Privacy;

public sealed class StorageAndServiceTests : IDisposable
{
    private const string Token = "a-sufficiently-long-token";
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    [Fact]
    public void Every_store_and_diagnostic_channel_has_a_protection_policy()
    {
        foreach (var kind in Enum.GetValues<DataStoreKind>())
        {
            var policy = DataStoreCatalog.For(kind);
            Assert.False(string.IsNullOrWhiteSpace(policy.Protection), kind.ToString());
            Assert.False(string.IsNullOrWhiteSpace(policy.Location), kind.ToString());
        }
        Assert.Equal(Enum.GetValues<DataStoreKind>().Length, DataStoreCatalog.All.Count);
        Assert.Contains(DataStoreCatalog.All, p => p.IsDiagnosticChannel && p.Kind == DataStoreKind.DiagnosticLogs);
    }

    [Fact]
    public void Source_derived_stores_are_confidential_and_the_privacy_document_names_each_store()
    {
        foreach (var kind in new[] { DataStoreKind.TokenSaveGraph, DataStoreKind.SearchIndex, DataStoreKind.EmbeddingCache, DataStoreKind.ExporterCheckpoint, DataStoreKind.DiagnosticLogs })
            Assert.True(DataStoreCatalog.For(kind).Confidential, kind.ToString());

        var doc = File.ReadAllText(Path.Combine(TestWorkspace.RepoRoot(), "docs", "privacy.md"));
        foreach (var kind in Enum.GetValues<DataStoreKind>())
            Assert.Contains($"`{kind}`", doc);
    }

    [Fact]
    public async Task Derived_data_is_created_owner_only_under_the_index_folder()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = ProtectedStorage.EnsurePrivateDirectory(_ws.Path, Path.Combine(_ws.Path, ".index", "cache", "vectors"));
        var file = Path.Combine(dir, "v.json");

        await ProtectedStorage.WritePrivateFileAsync(file, "{}", TestContext.Current.CancellationToken);

        ProtectedStorage.VerifyPrivate(dir);
        ProtectedStorage.VerifyPrivate(file);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        Assert.Equal("{}", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Group_or_world_access_is_detected()
    {
        if (OperatingSystem.IsWindows()) return;
        var file = _ws.Combine("open.txt");
        File.WriteAllText(file, "x");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

        Assert.Equal("STORAGE_NOT_PRIVATE", Assert.Throws<PrivacyPolicyException>(() => ProtectedStorage.VerifyPrivate(file)).Code);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("samples/corpus")]
    [InlineData("results")]
    public void Derived_data_outside_the_index_folder_is_refused(string relative) =>
        Assert.Equal("STORAGE_OUTSIDE_INDEX",
            Assert.Throws<PrivacyPolicyException>(() => ProtectedStorage.EnsurePrivateDirectory(_ws.Path, Path.Combine(_ws.Path, relative))).Code);

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("::1")]
    public void The_inference_service_binds_to_loopback_only(string host) =>
        Assert.True(IPAddress.IsLoopback(InferenceServiceSecurity.ValidateBindAddress(host)));

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("192.168.1.5")]
    [InlineData("embed.example.com")]
    public void A_non_loopback_bind_address_is_refused(string host) =>
        Assert.Equal("POLICY_BIND_NOT_LOCAL", Assert.Throws<PrivacyPolicyException>(() => InferenceServiceSecurity.ValidateBindAddress(host)).Code);

    [Fact]
    public void The_token_comes_from_the_environment_and_must_be_strong()
    {
        Assert.Equal("AUTH_TOKEN_MISSING", Assert.Throws<PrivacyPolicyException>(() => InferenceServiceSecurity.FromEnvironment(_ => null)).Code);
        Assert.Equal("AUTH_TOKEN_WEAK", Assert.Throws<PrivacyPolicyException>(() => InferenceServiceSecurity.FromEnvironment(_ => "short")).Code);
        Assert.True(InferenceServiceSecurity.FromEnvironment(v => v == InferenceServiceSecurity.TokenVariable ? Token : null).IsAuthorized("Bearer " + Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer ")]
    [InlineData("Bearer wrong-wrong-wrong-wrong")]
    [InlineData("Basic a-sufficiently-long-token")]
    [InlineData("bearer a-sufficiently-long-token")]
    public void Requests_without_the_right_bearer_token_are_not_authorized(string? header) =>
        Assert.False(new InferenceServiceSecurity(Token).IsAuthorized(header));

    [Fact]
    public void Request_limits_bound_body_and_batch_size()
    {
        var limits = new RequestLimits(MaxBodyBytes: 100, MaxBatchInputs: 2);

        limits.Check(100, 2);
        Assert.Equal("LIMIT_BODY_TOO_LARGE", Assert.Throws<PrivacyPolicyException>(() => limits.Check(101, 1)).Code);
        Assert.Equal("LIMIT_BATCH_TOO_LARGE", Assert.Throws<PrivacyPolicyException>(() => limits.Check(1, 3)).Code);
        Assert.Same(limits, new InferenceServiceSecurity(Token, limits).Limits);
    }
}

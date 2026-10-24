using System.Security.Cryptography;
using Polifemo.Platform.TokenSave;
using Polifemo.Tests.Support;

namespace Polifemo.Tests.Platform.TokenSave;

public class ReadOnlyGuaranteeTests : IDisposable
{
    private readonly TestWorkspace _ws = new();
    public void Dispose() => _ws.Dispose();

    private static ActiveDatabaseResolver Resolver => new((_, _) => Task.FromResult<string?>("main"));

    [Fact]
    public async Task Check_does_not_change_database_bytes_or_mtime()
    {
        new SyntheticTokenSave().Build(_ws.Path);
        var db = Path.Combine(_ws.Path, ".tokensave", "tokensave.db");
        var hash = SHA256.HashData(File.ReadAllBytes(db));
        var mtime = File.GetLastWriteTimeUtc(db);

        var report = await new CompatibilityChecker(Resolver, _ => Task.FromResult(PlatformPins.TokenSave))
            .CheckAsync(_ws.Path, PlatformPins.TokenSave, ct: TestContext.Current.CancellationToken);

        Assert.True(report.Ok);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(db)));
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(db));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(db)!, "*-wal"));
    }

    [Fact]
    public async Task Wal_mode_database_is_not_reported_as_modified_by_the_empty_sidecars_sqlite_creates()
    {
        new SyntheticTokenSave { WalMode = true }.Build(_ws.Path);
        var dir = Path.Combine(_ws.Path, ".tokensave");
        var db = Path.Combine(dir, "tokensave.db");
        Assert.Empty(Directory.GetFiles(dir, "*-wal"));
        var hash = SHA256.HashData(File.ReadAllBytes(db));

        var report = await new CompatibilityChecker(Resolver, _ => Task.FromResult(PlatformPins.TokenSave))
            .CheckAsync(_ws.Path, PlatformPins.TokenSave, ct: TestContext.Current.CancellationToken);

        Assert.True(report.Ok, string.Join(", ", report.Mismatches.Select(m => m.Code)));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(db)));
    }

    [Fact]
    public async Task Connection_cannot_write()
    {
        new SyntheticTokenSave().Build(_ws.Path);
        var db = Path.Combine(_ws.Path, ".tokensave", "tokensave.db");

        using var reader = TokenSaveReader.OpenReadOnly(db);

        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(() => reader.ExecuteForTest("INSERT INTO metadata(key,value) VALUES('x','y')"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Concurrent_modification_during_the_check_is_TS_DB_MODIFIED()
    {
        new SyntheticTokenSave().Build(_ws.Path);
        var db = Path.Combine(_ws.Path, ".tokensave", "tokensave.db");
        var checker = new CompatibilityChecker(Resolver, _ => Task.FromResult(PlatformPins.TokenSave))
        {
            AfterDatabaseRead = path => File.AppendAllText(path + "-wal", "x"),
        };

        var report = await checker.CheckAsync(_ws.Path, PlatformPins.TokenSave, ct: TestContext.Current.CancellationToken);

        Assert.False(report.Ok);
        Assert.Contains(report.Mismatches, m => m.Code == "TS_DB_MODIFIED");
        Assert.True(File.Exists(db));
    }
}

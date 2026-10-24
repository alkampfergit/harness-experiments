using System.ComponentModel;
using System.Text.RegularExpressions;
using Polifemo.Contracts.Errors;
using Polifemo.Platform.Processes;

namespace Polifemo.Platform.TokenSave;

/// <summary>
/// Compares the installed TokenSave and the active database with the pinned expectation before any
/// import (contracts/tokensave-schema.md). Every difference is reported; nothing is ever written.
/// </summary>
public sealed partial class CompatibilityChecker(
    ActiveDatabaseResolver resolver,
    Func<CancellationToken, Task<string>> binaryVersion)
{
    private readonly ExpectedSchema _schema = ExpectedSchema.Load();

    public CompatibilityChecker() : this(new ActiveDatabaseResolver(), TokenSaveBinary.VersionAsync) { }

    /// <summary>Test seam: runs after the database was read and before the second size/mtime snapshot.</summary>
    internal Action<string>? AfterDatabaseRead { get; init; }

    public async Task<CompatibilityReport> CheckAsync(string worktree, string expectedVersion, CancellationToken ct = default)
    {
        var mismatches = new List<Mismatch>();
        var warnings = new List<Mismatch>();

        var binary = await binaryVersion(ct);
        if (binary != expectedVersion)
            mismatches.Add(new Mismatch("TS_VERSION_MISMATCH", "tokensave --version", expectedVersion, binary));

        ResolvedDatabase db;
        try
        {
            db = await resolver.ResolveAsync(worktree, ct);
        }
        catch (ActiveDatabaseResolutionException ex)
        {
            mismatches.Add(new Mismatch(ex.Code, ".tokensave", null, ex.Message));
            return new CompatibilityReport(binary, null, null, null, mismatches, warnings);
        }

        var relative = Path.GetRelativePath(worktree, db.DatabasePath);
        var before = Snapshot(db.DatabasePath);
        string? indexVersion;
        using (var reader = TokenSaveReader.OpenReadOnly(db.DatabasePath))
        {
            indexVersion = Compare(reader, expectedVersion, mismatches, warnings);
        }
        AfterDatabaseRead?.Invoke(db.DatabasePath);
        if (Snapshot(db.DatabasePath) != before)
            mismatches.Add(new Mismatch("TS_DB_MODIFIED", relative, "unchanged", "size or mtime changed during the check"));

        return new CompatibilityReport(binary, indexVersion, relative, db.Resolution, mismatches, warnings);
    }

    private string? Compare(TokenSaveReader reader, string expectedVersion, List<Mismatch> mismatches, List<Mismatch> warnings)
    {
        var tables = reader.Tables();
        string? indexVersion = null;
        var checkIndexVersion = false;

        foreach (var (table, expectedColumns) in _schema.Tables)
        {
            if (!tables.Contains(table))
            {
                mismatches.Add(new Mismatch("TS_TABLE_MISSING", table));
                continue;
            }

            var actual = reader.Columns(table).ToDictionary(c => c.Name);
            foreach (var expected in expectedColumns)
            {
                var subject = $"{table}.{expected.Name}";
                if (!actual.TryGetValue(expected.Name, out var found))
                    mismatches.Add(new Mismatch("TS_COLUMN_MISSING", subject, Describe(expected.Type, expected.NotNull)));
                else if (!found.Type.Equals(expected.Type, StringComparison.OrdinalIgnoreCase) || found.NotNull != expected.NotNull)
                    mismatches.Add(new Mismatch("TS_COLUMN_CHANGED", subject,
                        Describe(expected.Type, expected.NotNull), Describe(found.Type, found.NotNull)));
            }

            foreach (var extra in actual.Keys.Except(expectedColumns.Select(c => c.Name)).Order(StringComparer.Ordinal))
                warnings.Add(new Mismatch("TS_EXTRA_COLUMN", $"{table}.{extra}"));

            if (table == "metadata" && actual.ContainsKey("key") && actual.ContainsKey("value"))
                checkIndexVersion = true;
        }

        foreach (var extra in tables.Except(_schema.Tables.Keys).Order(StringComparer.Ordinal))
            warnings.Add(new Mismatch("TS_EXTRA_TABLE", extra));

        if (checkIndexVersion)
        {
            indexVersion = reader.Metadata("last_full_index_version");
            if (indexVersion != expectedVersion)
                mismatches.Add(new Mismatch("TS_INDEX_VERSION_MISMATCH", "metadata.last_full_index_version",
                    expectedVersion, indexVersion));
        }

        return indexVersion;
    }

    private static string Describe(string type, bool notNull) => notNull ? $"{type} NOT NULL" : type;

    /// <summary>
    /// Opening a WAL-mode database read-only makes SQLite create an empty <c>-wal</c> (and <c>-shm</c>)
    /// next to it. That is not a change to the data, so a missing and an empty <c>-wal</c> count the
    /// same; any real content in the main file or the log still shows up.
    /// </summary>
    private static (FileSnapshot Db, FileSnapshot Wal) Snapshot(string path)
    {
        var wal = FileSnapshot.Of(path + "-wal");
        return (FileSnapshot.Of(path), wal.Length == 0 ? new FileSnapshot(false, 0, 0) : wal);
    }
}

public static partial class TokenSaveBinary
{
    [GeneratedRegex(@"\d+\.\d+\.\d+\S*")]
    private static partial Regex VersionPattern();

    public static async Task<string> VersionAsync(CancellationToken ct)
    {
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync("tokensave", ["--version"], ct: ct);
        }
        catch (Win32Exception ex)
        {
            throw new PolifemoException("TS_BINARY_MISSING", "The 'tokensave' binary could not be started.", ex);
        }

        var match = VersionPattern().Match(result.StdOut);
        return result.ExitCode == 0 && match.Success
            ? match.Value
            : throw new PolifemoException("TS_BINARY_MISSING", "'tokensave --version' did not report a version.");
    }
}

using System.Text.RegularExpressions;
using Polifemo.Platform.TokenSave;

namespace Polifemo.Tests.Platform.TokenSave;

/// <summary>TokenSave timestamps are never a change or publication cursor (FR-010, research R6).</summary>
public sealed class NoTimestampReadTests
{
    private static readonly string[] TimeColumns =
        ["modified_at", "indexed_at", "updated_at", "last_sync_at", "last_full_sync_at", "last_sync_duration_ms", "created_at", "last_synced_at"];

    [Fact]
    public void Reader_sql_selects_no_timestamp_column()
    {
        Assert.NotEmpty(TokenSaveReader.SqlStatements);
        foreach (var sql in TokenSaveReader.SqlStatements)
            foreach (var column in TimeColumns)
                Assert.DoesNotMatch($@"\b{Regex.Escape(column)}\b", sql);
    }

    [Theory]
    [InlineData(typeof(GraphSnapshot))]
    [InlineData(typeof(TokenSaveProvenance))]
    [InlineData(typeof(GraphRevision))]
    [InlineData(typeof(SourceFingerprint))]
    [InlineData(typeof(IntegrityReport))]
    [InlineData(typeof(DocAssociationRow))]
    [InlineData(typeof(TokenSaveGraph))]
    [InlineData(typeof(SymbolRow))]
    [InlineData(typeof(EdgeRow))]
    [InlineData(typeof(UnresolvedRow))]
    [InlineData(typeof(AmbiguousRow))]
    [InlineData(typeof(FileRow))]
    public void Snapshot_types_have_no_time_member(Type type)
    {
        foreach (var property in type.GetProperties())
        {
            Assert.NotEqual(typeof(DateTime), property.PropertyType);
            Assert.NotEqual(typeof(DateTimeOffset), property.PropertyType);
            Assert.False(property.Name.EndsWith("At", StringComparison.Ordinal), $"{type.Name}.{property.Name}");
        }
    }
}

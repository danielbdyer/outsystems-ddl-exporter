using System;
using System.Text.Json;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Diagnostics;

namespace Osm.Pipeline.Tests.Diagnostics;

public class ProfileSnapshotDebugFormatterTests
{
    [Fact]
    public void ToJson_ProducesIndentedSnapshot()
    {
        var schema = SchemaName.Create("dbo").Value;
        var table = TableName.Create("Customer").Value;
        var column = ColumnName.Create("Id").Value;
        var tenantColumn = ColumnName.Create("TenantId").Value;

        var columnProfile = ColumnProfile.Create(
            schema,
            table,
            column,
            isNullablePhysical: false,
            isComputed: false,
            isPrimaryKey: true,
            isUniqueKey: true,
            defaultDefinition: null,
            rowCount: 100,
            nullCount: 0,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, 100)).Value;

        var uniqueProfile = UniqueCandidateProfile.Create(
            schema,
            table,
            column,
            hasDuplicate: false,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, 100)).Value;

        var compositeProfile = CompositeUniqueCandidateProfile.Create(
            schema,
            table,
            new[] { column, tenantColumn },
            hasDuplicate: false).Value;

        var reference = ForeignKeyReference
            .Create(schema, table, column, schema, table, column, hasDatabaseConstraint: true)
            .Value;
        var foreignKeyProfile = ForeignKeyReality.Create(
            reference,
            hasOrphan: false,
            isNoCheck: false,
            ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, 100)).Value;

        var snapshot = ProfileSnapshot.Create(
            new[] { columnProfile },
            new[] { uniqueProfile },
            new[] { compositeProfile },
            new[] { foreignKeyProfile }).Value;

        var json = ProfileSnapshotDebugFormatter.ToJson(snapshot);
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("Columns", out var columns));
        Assert.Equal(1, columns.GetArrayLength());
        Assert.True(document.RootElement.TryGetProperty("UniqueCandidates", out var uniques));
        Assert.Equal(1, uniques.GetArrayLength());
        Assert.True(document.RootElement.TryGetProperty("CompositeUniqueCandidates", out var composites));
        Assert.Equal(1, composites.GetArrayLength());
        Assert.True(document.RootElement.TryGetProperty("ForeignKeys", out var foreignKeys));
        Assert.Equal(1, foreignKeys.GetArrayLength());
    }
}

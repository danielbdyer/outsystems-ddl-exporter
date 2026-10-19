using System.Linq;
using System.Text;
using Osm.Domain.Profiling;
using Osm.Json;
using Tests.Support;

namespace Osm.Json.Tests;

public sealed class ProfileSnapshotDeserializerTests
{
    private readonly ProfileSnapshotDeserializer _deserializer = new();

    [Fact]
    public void Deserialize_ShouldParseEdgeCaseFixture()
    {
        using var stream = FixtureFile.OpenStream("profiling/profile.edge-case.json");

        var result = _deserializer.Deserialize(stream);

        Assert.True(result.IsSuccess);
        var snapshot = result.Value;
        Assert.Equal(14, snapshot.Columns.Length);

        var email = snapshot.Columns.Single(c => c.Table.Value == "OSUSR_ABC_CUSTOMER" && c.Column.Value == "EMAIL");
        Assert.True(email.IsUniqueKey);
        Assert.Equal(0, email.NullCount);
        Assert.Equal(ProfilingProbeOutcome.Succeeded, email.NullCountStatus.Outcome);

        var triggered = snapshot.Columns.Single(c => c.Table.Value == "OSUSR_XYZ_JOBRUN" && c.Column.Value == "TRIGGEREDBYUSERID");
        Assert.Equal(950_000, triggered.NullCount);
        Assert.Equal(ProfilingProbeOutcome.Succeeded, triggered.NullCountStatus.Outcome);

        var fk = snapshot.ForeignKeys.Single(f => f.Reference.FromTable.Value == "OSUSR_XYZ_JOBRUN");
        Assert.True(fk.HasOrphan);
        Assert.False(fk.Reference.HasDatabaseConstraint);
        Assert.True(fk.IsNoCheck);
        Assert.Equal(ProfilingProbeOutcome.Succeeded, fk.ProbeStatus.Outcome);

        var accountNumber = snapshot.UniqueCandidates.Single(u => u.Table.Value == "BILLING_ACCOUNT");
        Assert.False(accountNumber.HasDuplicate);
        Assert.Equal(ProfilingProbeOutcome.Succeeded, accountNumber.ProbeStatus.Outcome);

        Assert.Empty(snapshot.CompositeUniqueCandidates);
    }

    [Fact]
    public void Deserialize_ShouldFailWhenColumnsMissing()
    {
        const string json = "{\"uniqueCandidates\":[],\"fkReality\":[]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = _deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("profile.columns.missing", error.Code);
    }

    [Fact]
    public void Deserialize_ShouldParseCompositeUniqueCandidates()
    {
        using var stream = FixtureFile.OpenStream("profiling/profile.micro-unique-composite.json");

        var result = _deserializer.Deserialize(stream);

        Assert.True(result.IsSuccess);
        var composite = Assert.Single(result.Value.CompositeUniqueCandidates);
        Assert.False(composite.HasDuplicate);
        Assert.Equal(2, composite.Columns.Length);
    }

    [Fact]
    public void Deserialize_ShouldIncludeCoordinateMetadata_WhenSchemaInvalid()
    {
        const string json = """
        {
          "columns": [
            {
              "Schema": "",
              "Table": "Users",
              "Column": "Id",
              "IsNullablePhysical": false,
              "IsComputed": false,
              "IsPrimaryKey": true,
              "IsUniqueKey": true,
              "RowCount": 0,
              "NullCount": 0
            }
          ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = _deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("schema.name.invalid", error.Code);
        Assert.Equal(string.Empty, error.Metadata["schema"]);
        Assert.Equal("Users", error.Metadata["table"]);
        Assert.Equal("Id", error.Metadata["column"]);
    }

    [Fact]
    public void Deserialize_ShouldIncludeForeignKeyMetadata_WhenReferenceSchemaInvalid()
    {
        const string json = """
        {
          "columns": [],
          "fkReality": [
            {
              "Ref": {
                "FromSchema": "",
                "FromTable": "Source",
                "FromColumn": "SourceId",
                "ToSchema": "dbo",
                "ToTable": "Target",
                "ToColumn": "TargetId",
                "HasDbConstraint": false
              },
              "HasOrphan": false,
              "IsNoCheck": false
            }
          ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = _deserializer.Deserialize(stream);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("schema.name.invalid", error.Code);
        Assert.Equal(string.Empty, error.Metadata["from.schema"]);
        Assert.Equal("Source", error.Metadata["from.table"]);
        Assert.Equal("SourceId", error.Metadata["from.column"]);
    }
}

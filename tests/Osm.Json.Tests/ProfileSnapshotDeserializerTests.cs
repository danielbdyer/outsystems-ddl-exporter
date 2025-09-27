using System.Linq;
using System.Text;
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

        var triggered = snapshot.Columns.Single(c => c.Table.Value == "OSUSR_XYZ_JOBRUN" && c.Column.Value == "TRIGGEREDBYUSERID");
        Assert.Equal(950_000, triggered.NullCount);

        var fk = snapshot.ForeignKeys.Single(f => f.Reference.FromTable.Value == "OSUSR_XYZ_JOBRUN");
        Assert.True(fk.HasOrphan);
        Assert.False(fk.Reference.HasDatabaseConstraint);

        var accountNumber = snapshot.UniqueCandidates.Single(u => u.Table.Value == "BILLING_ACCOUNT");
        Assert.False(accountNumber.HasDuplicate);

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
}

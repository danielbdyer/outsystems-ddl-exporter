using System.Linq;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class ColumnIdentityTests
{
    [Fact]
    public void FromEntityAndAttribute_PopulatesMetadata()
    {
        var model = ModelFixtures.LoadModel("model.micro-fk-protect.json");
        var entity = model.Modules.SelectMany(m => m.Entities).Single(e => e.LogicalName.Value == "Child");
        var attribute = entity.Attributes.Single(a => a.LogicalName.Value == "ParentId");

        var identity = ColumnIdentity.From(entity, attribute);

        Assert.Equal(entity.Module.Value, identity.ModuleName);
        Assert.Equal(entity.LogicalName.Value, identity.EntityName);
        Assert.Equal(entity.PhysicalName.Value, identity.TableName);
        Assert.Equal(attribute.LogicalName.Value, identity.AttributeName);
        Assert.Equal(attribute.ColumnName, identity.Coordinate.Column);
    }

    [Fact]
    public void FromColumnProfile_ResolvesUsingAttributeIndex()
    {
        var model = ModelFixtures.LoadModel("model.micro-fk-protect.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroFkProtect);
        var index = EntityAttributeIndex.Create(model);
        var profile = snapshot.Columns.Single(p => p.Column.Value == "PARENTID");

        var identity = ColumnIdentity.From(profile, index);

        Assert.Equal("Child", identity.EntityName);
        Assert.Equal("ParentId", identity.AttributeName);
    }

    [Fact]
    public void FromForeignKeyReference_ResolvesReferencingColumn()
    {
        var model = ModelFixtures.LoadModel("model.micro-fk-protect.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroFkProtect);
        var index = EntityAttributeIndex.Create(model);
        var foreignKey = snapshot.ForeignKeys.Single(fk => fk.Reference.FromColumn.Value == "PARENTID");

        var identity = ColumnIdentity.From(foreignKey.Reference, index);

        Assert.Equal(foreignKey.Reference.FromSchema, identity.Coordinate.Schema);
        Assert.Equal(foreignKey.Reference.FromTable, identity.Coordinate.Table);
        Assert.Equal("ParentId", identity.AttributeName);
    }
}

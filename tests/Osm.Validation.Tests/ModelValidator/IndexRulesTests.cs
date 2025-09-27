using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Validation;

namespace Osm.Validation.Tests.ModelValidation;

public class IndexRulesTests
{
    private readonly ModelValidator _validator = new();

    [Fact]
    public void Validate_ShouldFail_WhenIndexReferencesMissingAttribute()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var module = model.Modules.First();
        var entity = module.Entities.First(e => e.Indexes.Any());
        var index = entity.Indexes.First();
        var missingColumn = new IndexColumnModel(
            AttributeName.Create("MissingAttribute").Value,
            ColumnName.Create("MISSING_COLUMN").Value,
            1);
        var mutatedIndex = index with { Columns = index.Columns.Replace(index.Columns[0], missingColumn) };
        var indexes = entity.Indexes.Replace(index, mutatedIndex);
        var mutatedEntity = entity with { Indexes = indexes };
        var mutatedModule = module with { Entities = module.Entities.Replace(entity, mutatedEntity) };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var report = _validator.Validate(mutatedModel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Messages, m => m.Code == "entity.indexes.columnMissing");
    }

    [Fact]
    public void Validate_ShouldFail_WhenIndexOrdinalsHaveGaps()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var module = model.Modules.First();
        var entity = module.Entities.First(e => e.Indexes.Any(i => i.Columns.Length > 1));
        var index = entity.Indexes.First(i => i.Columns.Length > 1);
        var columns = index.Columns.ToArray();
        columns[1] = columns[1] with { Ordinal = 3 };
        var mutatedIndex = index with { Columns = columns.ToImmutableArray() };
        var indexes = entity.Indexes.Replace(index, mutatedIndex);
        var mutatedEntity = entity with { Indexes = indexes };
        var mutatedModule = module with { Entities = module.Entities.Replace(entity, mutatedEntity) };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var report = _validator.Validate(mutatedModel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Messages, m => m.Code == "entity.indexes.ordinalGap");
    }
}

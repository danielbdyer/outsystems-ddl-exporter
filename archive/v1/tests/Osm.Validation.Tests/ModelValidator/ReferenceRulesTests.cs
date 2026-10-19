using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Validation;

namespace Osm.Validation.Tests.ModelValidation;

public class ReferenceRulesTests
{
    private readonly ModelValidator _validator = new();

    [Fact]
    public void Validate_ShouldFail_WhenReferenceMetadataMissing()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var module = model.Modules.First();
        var entity = module.Entities.First(e => e.Attributes.Any(a => a.Reference.IsReference));
        var referenceAttribute = entity.Attributes.First(a => a.Reference.IsReference);
        var mutatedAttribute = referenceAttribute with
        {
            Reference = new AttributeReference(true, referenceAttribute.Reference.TargetEntityId, null, null, referenceAttribute.Reference.DeleteRuleCode, referenceAttribute.Reference.HasDatabaseConstraint)
        };
        var attributes = entity.Attributes.Replace(referenceAttribute, mutatedAttribute);
        var mutatedEntity = entity with { Attributes = attributes };
        var mutatedModule = module with { Entities = module.Entities.Replace(entity, mutatedEntity) };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var report = _validator.Validate(mutatedModel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Messages, m => m.Code == "entity.reference.metadataMissing");
    }

    [Fact]
    public void Validate_ShouldFail_WhenReferenceTargetsUnknownEntity()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var module = model.Modules.First();
        var entity = module.Entities.First(e => e.Attributes.Any(a => a.Reference.IsReference));
        var referenceAttribute = entity.Attributes.First(a => a.Reference.IsReference);
        var mutatedAttribute = referenceAttribute with
        {
            Reference = new AttributeReference(
                true,
                9999,
                EntityName.Create("MissingEntity").Value,
                TableName.Create("OSUSR_MISSING_ENTITY").Value,
                referenceAttribute.Reference.DeleteRuleCode,
                referenceAttribute.Reference.HasDatabaseConstraint)
        };
        var attributes = entity.Attributes.Replace(referenceAttribute, mutatedAttribute);
        var mutatedEntity = entity with { Attributes = attributes };
        var mutatedModule = module with { Entities = module.Entities.Replace(entity, mutatedEntity) };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var report = _validator.Validate(mutatedModel);

        Assert.False(report.IsValid);
        Assert.Contains(report.Messages, m => m.Code == "entity.reference.targetMissing");
        Assert.Contains(report.Messages, m => m.Code == "entity.reference.targetPhysicalMissing");
    }

    [Fact]
    public void Validate_ShouldWarn_WhenDbConstraintCrossesSchema()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var module = model.Modules.First(m => m.Entities.Any(e => e.LogicalName.Value == "Customer"));
        var city = module.Entities.First(e => e.LogicalName.Value == "City");
        var customer = module.Entities.First(e => e.LogicalName.Value == "Customer");

        var lookupSchema = SchemaName.Create("lookup").Value;
        var mutatedCity = city with { Schema = lookupSchema };
        var mutatedModule = module with { Entities = module.Entities.Replace(city, mutatedCity) };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var report = _validator.Validate(mutatedModel);

        Assert.True(report.IsValid);
        var warning = Assert.Single(report.Messages, m => m.Code == "entity.reference.crossSchemaConstraint");
        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
        Assert.Equal($"modules[{module.Name.Value}].entities[{customer.LogicalName.Value}].attributes[CityId]", warning.Path);
    }

    [Fact]
    public void Validate_ShouldWarn_WhenDbConstraintCrossesCatalog()
    {
        var model = ValidationModelFixture.CreateValidModel();
        var module = model.Modules.First(m => m.Entities.Any(e => e.LogicalName.Value == "Customer"));
        var city = module.Entities.First(e => e.LogicalName.Value == "City");
        var customer = module.Entities.First(e => e.LogicalName.Value == "Customer");

        var mutatedCustomer = customer with { Catalog = "CustomerCatalog" };
        var mutatedCity = city with { Catalog = "LookupCatalog" };
        var mutatedEntities = module.Entities.Replace(customer, mutatedCustomer).Replace(city, mutatedCity);
        var mutatedModule = module with { Entities = mutatedEntities };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var report = _validator.Validate(mutatedModel);

        Assert.True(report.IsValid);
        var warning = Assert.Single(report.Messages, m => m.Code == "entity.reference.crossCatalogConstraint");
        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
        Assert.Equal($"modules[{module.Name.Value}].entities[{customer.LogicalName.Value}].attributes[CityId]", warning.Path);
    }
}

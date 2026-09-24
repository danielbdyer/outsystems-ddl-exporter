using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;

namespace Osm.Validation;

public interface IModelValidator
{
    ModelValidationReport Validate(OsmModel model);
}

public sealed class ModelValidator : IModelValidator
{
    public ModelValidationReport Validate(OsmModel model)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var messages = ImmutableArray.CreateBuilder<ModelValidationMessage>();

        ValidateModules(model, messages);

        return messages.Count == 0
            ? ModelValidationReport.Empty
            : new ModelValidationReport(messages.ToImmutable());
    }

    private static void ValidateModules(OsmModel model, ImmutableArray<ModelValidationMessage>.Builder messages)
    {
        if (model.Modules.IsDefaultOrEmpty || model.Modules.Length == 0)
        {
            messages.Add(ModelValidationMessage.Error(
                "model.modules.empty",
                "Model must contain at least one module.",
                "modules"));
            return;
        }

        var logicalEntityLookup = model.Modules
            .SelectMany(m => m.Entities)
            .ToDictionary(e => e.LogicalName.Value, e => e, StringComparer.Ordinal);

        var physicalEntityLookup = model.Modules
            .SelectMany(m => m.Entities)
            .ToDictionary(e => e.PhysicalName.Value, e => e, StringComparer.OrdinalIgnoreCase);

        foreach (var module in model.Modules)
        {
            ValidateModule(module, messages, logicalEntityLookup, physicalEntityLookup);
        }
    }

    private static void ValidateModule(
        ModuleModel module,
        ImmutableArray<ModelValidationMessage>.Builder messages,
        IReadOnlyDictionary<string, EntityModel> logicalEntityLookup,
        IReadOnlyDictionary<string, EntityModel> physicalEntityLookup)
    {
        if (module.Entities.IsDefaultOrEmpty || module.Entities.Length == 0)
        {
            messages.Add(ModelValidationMessage.Error(
                "module.entities.empty",
                "Module must contain at least one entity.",
                ModulePath(module)));
            return;
        }

        foreach (var entity in module.Entities)
        {
            ValidateEntity(module, entity, messages, logicalEntityLookup, physicalEntityLookup);
        }
    }

    private static void ValidateEntity(
        ModuleModel module,
        EntityModel entity,
        ImmutableArray<ModelValidationMessage>.Builder messages,
        IReadOnlyDictionary<string, EntityModel> logicalEntityLookup,
        IReadOnlyDictionary<string, EntityModel> physicalEntityLookup)
    {
        EnsureAttributeUniqueness(module, entity, messages);
        EnsureIdentifierIntegrity(module, entity, messages);
        EnsureReferenceSanity(module, entity, messages, logicalEntityLookup, physicalEntityLookup);
        EnsureIndexIntegrity(module, entity, messages);
    }

    private static void EnsureAttributeUniqueness(ModuleModel module, EntityModel entity, ImmutableArray<ModelValidationMessage>.Builder messages)
    {
        var logicalGroups = entity.Attributes
            .GroupBy(a => a.LogicalName.Value, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (var group in logicalGroups)
        {
            messages.Add(ModelValidationMessage.Error(
                "entity.attributes.duplicateLogical",
                $"Entity '{entity.LogicalName.Value}' contains duplicate attribute logical name '{group.Key}'.",
                EntityPath(module, entity)));
        }

        var physicalGroups = entity.Attributes
            .GroupBy(a => a.ColumnName.Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in physicalGroups)
        {
            messages.Add(ModelValidationMessage.Error(
                "entity.attributes.duplicatePhysical",
                $"Entity '{entity.LogicalName.Value}' contains duplicate attribute physical name '{group.Key}'.",
                EntityPath(module, entity)));
        }
    }

    private static void EnsureIdentifierIntegrity(ModuleModel module, EntityModel entity, ImmutableArray<ModelValidationMessage>.Builder messages)
    {
        var identifiers = entity.Attributes.Where(a => a.IsIdentifier).ToList();
        if (identifiers.Count == 0)
        {
            messages.Add(ModelValidationMessage.Error(
                "entity.identifier.missing",
                $"Entity '{entity.LogicalName.Value}' must define exactly one identifier attribute.",
                EntityPath(module, entity)));
            return;
        }

        if (identifiers.Count > 1)
        {
            messages.Add(ModelValidationMessage.Error(
                "entity.identifier.multiple",
                $"Entity '{entity.LogicalName.Value}' defines {identifiers.Count} identifier attributes; expected exactly one.",
                EntityPath(module, entity)));
            return;
        }

        var identifier = identifiers[0];
        if (!string.Equals(identifier.DataType, "Identifier", StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(ModelValidationMessage.Error(
                "entity.identifier.typeMismatch",
                $"Identifier attribute '{identifier.LogicalName.Value}' must have data type 'Identifier'.",
                AttributePath(module, entity, identifier)));
        }
    }

    private static void EnsureReferenceSanity(
        ModuleModel module,
        EntityModel entity,
        ImmutableArray<ModelValidationMessage>.Builder messages,
        IReadOnlyDictionary<string, EntityModel> logicalEntityLookup,
        IReadOnlyDictionary<string, EntityModel> physicalEntityLookup)
    {
        foreach (var attribute in entity.Attributes)
        {
            if (!attribute.Reference.IsReference)
            {
                continue;
            }

            if (attribute.Reference.TargetEntity is null || attribute.Reference.TargetPhysicalName is null)
            {
                messages.Add(ModelValidationMessage.Error(
                    "entity.reference.metadataMissing",
                    $"Reference attribute '{attribute.LogicalName.Value}' must supply target entity metadata.",
                    AttributePath(module, entity, attribute)));
                continue;
            }

            var targetLogical = attribute.Reference.TargetEntity.Value.Value;
            logicalEntityLookup.TryGetValue(targetLogical, out var logicalTarget);
            if (logicalTarget is null)
            {
                messages.Add(ModelValidationMessage.Error(
                    "entity.reference.targetMissing",
                    $"Reference attribute '{attribute.LogicalName.Value}' points to unknown entity '{targetLogical}'.",
                    AttributePath(module, entity, attribute)));
            }

            var targetPhysical = attribute.Reference.TargetPhysicalName.Value.Value;
            physicalEntityLookup.TryGetValue(targetPhysical, out var physicalTarget);
            if (physicalTarget is null)
            {
                messages.Add(ModelValidationMessage.Error(
                    "entity.reference.targetPhysicalMissing",
                    $"Reference attribute '{attribute.LogicalName.Value}' points to unknown physical table '{targetPhysical}'.",
                    AttributePath(module, entity, attribute)));
            }

            var resolvedTarget = logicalTarget ?? physicalTarget;
            var relationshipConstraint = entity.Relationships
                .FirstOrDefault(r => r.ViaAttribute == attribute.LogicalName)?.HasDatabaseConstraint == true;
            var wantsConstraint = attribute.Reference.HasDatabaseConstraint || relationshipConstraint;

            if (!wantsConstraint || resolvedTarget is null)
            {
                continue;
            }

            if (!string.Equals(entity.Schema.Value, resolvedTarget.Schema.Value, StringComparison.OrdinalIgnoreCase))
            {
                messages.Add(ModelValidationMessage.Warning(
                    "entity.reference.crossSchemaConstraint",
                    $"Reference attribute '{attribute.LogicalName.Value}' enforces a database constraint but spans schemas '{entity.Schema.Value}' → '{resolvedTarget.Schema.Value}'.",
                    AttributePath(module, entity, attribute)));
            }

            if (!CatalogsMatch(entity.Catalog, resolvedTarget.Catalog))
            {
                var sourceCatalog = string.IsNullOrWhiteSpace(entity.Catalog) ? "<default>" : entity.Catalog!;
                var targetCatalog = string.IsNullOrWhiteSpace(resolvedTarget.Catalog) ? "<default>" : resolvedTarget.Catalog!;
                messages.Add(ModelValidationMessage.Warning(
                    "entity.reference.crossCatalogConstraint",
                    $"Reference attribute '{attribute.LogicalName.Value}' enforces a database constraint but spans catalogs '{sourceCatalog}' → '{targetCatalog}'.",
                    AttributePath(module, entity, attribute)));
            }
        }
    }

    private static bool CatalogsMatch(string? left, string? right)
        => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static void EnsureIndexIntegrity(ModuleModel module, EntityModel entity, ImmutableArray<ModelValidationMessage>.Builder messages)
    {
        if (entity.Indexes.IsDefaultOrEmpty)
        {
            return;
        }

        var attributeByLogical = new Dictionary<string, AttributeModel>(StringComparer.Ordinal);
        var attributeByPhysical = new Dictionary<string, AttributeModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in entity.Attributes)
        {
            attributeByLogical.TryAdd(attribute.LogicalName.Value, attribute);
            attributeByPhysical.TryAdd(attribute.ColumnName.Value, attribute);
        }

        foreach (var index in entity.Indexes)
        {
            var ordinals = index.Columns.Select(c => c.Ordinal).OrderBy(v => v).ToArray();
            for (var expected = 1; expected <= ordinals.Length; expected++)
            {
                if (expected != ordinals[expected - 1])
                {
                    messages.Add(ModelValidationMessage.Error(
                        "entity.indexes.ordinalGap",
                        $"Index '{index.Name.Value}' on entity '{entity.LogicalName.Value}' must have contiguous ordinals starting at 1.",
                        IndexPath(module, entity, index)));
                    break;
                }
            }

            foreach (var column in index.Columns)
            {
                if (!attributeByLogical.TryGetValue(column.Attribute.Value, out var attribute) &&
                    !attributeByPhysical.TryGetValue(column.Column.Value, out attribute))
                {
                    messages.Add(ModelValidationMessage.Error(
                        "entity.indexes.columnMissing",
                        $"Index '{index.Name.Value}' references attribute '{column.Attribute.Value}'/'{column.Column.Value}' that does not exist on entity '{entity.LogicalName.Value}'.",
                        IndexColumnPath(module, entity, index, column)));
                    continue;
                }

                if (!string.Equals(attribute.ColumnName.Value, column.Column.Value, StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(ModelValidationMessage.Error(
                        "entity.indexes.columnMismatch",
                        $"Index '{index.Name.Value}' references physical column '{column.Column.Value}' but attribute '{attribute.LogicalName.Value}' is mapped to '{attribute.ColumnName.Value}'.",
                        IndexColumnPath(module, entity, index, column)));
                }
            }
        }
    }

    private static string ModulePath(ModuleModel module) => $"modules[{module.Name.Value}]";

    private static string EntityPath(ModuleModel module, EntityModel entity)
        => $"{ModulePath(module)}.entities[{entity.LogicalName.Value}]";

    private static string AttributePath(ModuleModel module, EntityModel entity, AttributeModel attribute)
        => $"{EntityPath(module, entity)}.attributes[{attribute.LogicalName.Value}]";

    private static string IndexPath(ModuleModel module, EntityModel entity, IndexModel index)
        => $"{EntityPath(module, entity)}.indexes[{index.Name.Value}]";

    private static string IndexColumnPath(ModuleModel module, EntityModel entity, IndexModel index, IndexColumnModel column)
        => $"{IndexPath(module, entity, index)}.columns[{column.Attribute.Value}]";
}

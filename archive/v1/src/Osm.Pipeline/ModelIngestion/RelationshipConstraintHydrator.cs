using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;

namespace Osm.Pipeline.ModelIngestion;

public interface IRelationshipConstraintHydrator
{
    Task<Result<OsmModel>> HydrateAsync(
        OsmModel model,
        ModelIngestionSqlMetadataOptions sqlOptions,
        ICollection<string>? warnings,
        CancellationToken cancellationToken);
}

internal sealed class RelationshipConstraintHydrator : IRelationshipConstraintHydrator
{
    private readonly IRelationshipConstraintMetadataProvider _metadataProvider;

    public RelationshipConstraintHydrator(IRelationshipConstraintMetadataProvider metadataProvider)
    {
        _metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
    }

    public async Task<Result<OsmModel>> HydrateAsync(
        OsmModel model,
        ModelIngestionSqlMetadataOptions sqlOptions,
        ICollection<string>? warnings,
        CancellationToken cancellationToken)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (sqlOptions is null)
        {
            throw new ArgumentNullException(nameof(sqlOptions));
        }

        var pending = CollectMissingConstraints(model, warnings);
        if (pending.Count == 0)
        {
            return Result<OsmModel>.Success(model);
        }

        var metadata = await _metadataProvider
            .LoadAsync(pending.Keys, sqlOptions, cancellationToken)
            .ConfigureAwait(false);

        var metadataLookup = metadata
            .GroupBy(static entry => entry.Constraint, RelationshipConstraintKeyComparer.Instance)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static item => item.Ordinal)
                    .ToImmutableArray(),
                RelationshipConstraintKeyComparer.Instance);

        var entityLookup = BuildEntityLookup(model);
        var enriched = ApplyMetadata(model, pending, metadataLookup, entityLookup, warnings);

        return Result<OsmModel>.Success(enriched);
    }

    private static Dictionary<RelationshipConstraintKey, List<MissingConstraintContext>> CollectMissingConstraints(
        OsmModel model,
        ICollection<string>? warnings)
    {
        var pending = new Dictionary<RelationshipConstraintKey, List<MissingConstraintContext>>(RelationshipConstraintKeyComparer.Instance);

        for (var moduleIndex = 0; moduleIndex < model.Modules.Length; moduleIndex++)
        {
            var module = model.Modules[moduleIndex];
            for (var entityIndex = 0; entityIndex < module.Entities.Length; entityIndex++)
            {
                var entity = module.Entities[entityIndex];
                if (entity.Relationships.IsDefaultOrEmpty)
                {
                    continue;
                }

                for (var relationshipIndex = 0; relationshipIndex < entity.Relationships.Length; relationshipIndex++)
                {
                    var relationship = entity.Relationships[relationshipIndex];
                    if (relationship.ActualConstraints.IsDefaultOrEmpty)
                    {
                        continue;
                    }

                    for (var constraintIndex = 0; constraintIndex < relationship.ActualConstraints.Length; constraintIndex++)
                    {
                        var constraint = relationship.ActualConstraints[constraintIndex];
                        if (!NeedsHydration(constraint))
                        {
                            continue;
                        }

                        var key = new RelationshipConstraintKey(entity.Schema.Value, entity.PhysicalName.Value, constraint.Name);
                        if (!key.IsValid)
                        {
                            warnings?.Add($"Relationship '{module.Name.Value}.{entity.LogicalName.Value}.{relationship.ViaAttribute.Value}' is missing constraint name metadata.");
                            continue;
                        }

                        if (!pending.TryGetValue(key, out var contexts))
                        {
                            contexts = new List<MissingConstraintContext>();
                            pending[key] = contexts;
                        }

                        contexts.Add(new MissingConstraintContext(moduleIndex, entityIndex, relationshipIndex, constraintIndex));
                    }
                }
            }
        }

        return pending;
    }

    private static bool NeedsHydration(RelationshipActualConstraint constraint)
    {
        if (constraint.Columns.IsDefaultOrEmpty)
        {
            return true;
        }

        return constraint.Columns.All(static column =>
            string.IsNullOrWhiteSpace(column.OwnerColumn) || string.IsNullOrWhiteSpace(column.ReferencedColumn));
    }

    private static OsmModel ApplyMetadata(
        OsmModel model,
        IReadOnlyDictionary<RelationshipConstraintKey, List<MissingConstraintContext>> pending,
        IReadOnlyDictionary<RelationshipConstraintKey, ImmutableArray<ForeignKeyColumnMetadata>> metadata,
        IReadOnlyDictionary<EntityLookupKey, EntityModel> entityLookup,
        ICollection<string>? warnings)
    {
        if (pending.Count == 0)
        {
            return model;
        }

        var moduleBuilder = model.Modules.ToBuilder();
        var missingMetadata = new HashSet<RelationshipConstraintKey>(RelationshipConstraintKeyComparer.Instance);
        var changed = false;

        foreach (var (key, contexts) in pending)
        {
            if (!metadata.TryGetValue(key, out var columns) || columns.IsDefaultOrEmpty)
            {
                if (missingMetadata.Add(key))
                {
                    warnings?.Add($"Unable to hydrate foreign key '{key.ConstraintName}' on '{key.Schema}.{key.Table}' because SQL metadata was not found.");
                }

                continue;
            }

            foreach (var context in contexts)
            {
                var module = moduleBuilder[context.ModuleIndex];
                var entityBuilder = module.Entities.ToBuilder();
                var entity = entityBuilder[context.EntityIndex];
                var relationships = entity.Relationships.ToBuilder();
                var relationship = relationships[context.RelationshipIndex];
                var constraints = relationship.ActualConstraints.ToBuilder();
                var constraint = constraints[context.ConstraintIndex];

                var resolvedColumns = BuildColumns(entity, columns, entityLookup);
                if (resolvedColumns.IsDefaultOrEmpty)
                {
                    continue;
                }

                constraints[context.ConstraintIndex] = constraint with { Columns = resolvedColumns };
                relationships[context.RelationshipIndex] = relationship with { ActualConstraints = constraints.ToImmutable() };
                entityBuilder[context.EntityIndex] = entity with { Relationships = relationships.ToImmutable() };
                moduleBuilder[context.ModuleIndex] = module with { Entities = entityBuilder.ToImmutable() };
                changed = true;
            }
        }

        return changed ? model with { Modules = moduleBuilder.ToImmutable() } : model;
    }

    private static ImmutableArray<RelationshipActualConstraintColumn> BuildColumns(
        EntityModel entity,
        ImmutableArray<ForeignKeyColumnMetadata> metadata,
        IReadOnlyDictionary<EntityLookupKey, EntityModel> entityLookup)
    {
        if (metadata.IsDefaultOrEmpty)
        {
            return ImmutableArray<RelationshipActualConstraintColumn>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<RelationshipActualConstraintColumn>(metadata.Length);
        var referencedKey = new EntityLookupKey(metadata[0].ReferencedSchema, metadata[0].ReferencedTable);
        entityLookup.TryGetValue(referencedKey, out var referencedEntity);

        foreach (var column in metadata)
        {
            var ownerAttribute = ResolveAttributeName(entity, column.OwnerColumn);
            var referencedAttribute = referencedEntity is null
                ? string.Empty
                : ResolveAttributeName(referencedEntity, column.ReferencedColumn);

            builder.Add(RelationshipActualConstraintColumn.Create(
                column.OwnerColumn,
                ownerAttribute,
                column.ReferencedColumn,
                referencedAttribute,
                column.Ordinal));
        }

        return builder.ToImmutable();
    }

    private static string ResolveAttributeName(EntityModel entity, string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return string.Empty;
        }

        var match = entity.Attributes.FirstOrDefault(attribute =>
            string.Equals(attribute.ColumnName.Value, columnName, StringComparison.OrdinalIgnoreCase));

        return match?.LogicalName.Value ?? string.Empty;
    }

    private static IReadOnlyDictionary<EntityLookupKey, EntityModel> BuildEntityLookup(OsmModel model)
    {
        var lookup = new Dictionary<EntityLookupKey, EntityModel>(EntityLookupKeyComparer.Instance);
        foreach (var module in model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                var key = new EntityLookupKey(entity.Schema.Value, entity.PhysicalName.Value);
                lookup[key] = entity;
            }
        }

        return lookup;
    }

    private readonly record struct MissingConstraintContext(
        int ModuleIndex,
        int EntityIndex,
        int RelationshipIndex,
        int ConstraintIndex);

    private readonly record struct EntityLookupKey(string Schema, string Table);

    private sealed class EntityLookupKeyComparer : IEqualityComparer<EntityLookupKey>
    {
        public static EntityLookupKeyComparer Instance { get; } = new();

        public bool Equals(EntityLookupKey x, EntityLookupKey y)
        {
            return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(EntityLookupKey obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table ?? string.Empty));
        }
    }
}

internal sealed class NullRelationshipConstraintHydrator : IRelationshipConstraintHydrator
{
    public static NullRelationshipConstraintHydrator Instance { get; } = new();

    public Task<Result<OsmModel>> HydrateAsync(
        OsmModel model,
        ModelIngestionSqlMetadataOptions sqlOptions,
        ICollection<string>? warnings,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<OsmModel>.Success(model));
    }
}

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public sealed record ModuleModel(
    ModuleName Name,
    bool IsSystemModule,
    bool IsActive,
    ImmutableArray<EntityModel> Entities)
{
    public static Result<ModuleModel> Create(
        ModuleName name,
        bool isSystemModule,
        bool isActive,
        IEnumerable<EntityModel> entities)
    {
        if (entities is null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        var materialized = entities.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            return Result<ModuleModel>.Failure(ValidationError.Create("module.entities.empty", "Module must contain at least one entity."));
        }

        if (HasDuplicates(materialized.Select(e => e.LogicalName.Value)))
        {
            return Result<ModuleModel>.Failure(ValidationError.Create("module.entities.duplicateLogical", "Module contains duplicate entity logical names."));
        }

        if (HasDuplicates(materialized.Select(e => e.PhysicalName.Value), StringComparer.OrdinalIgnoreCase))
        {
            return Result<ModuleModel>.Failure(ValidationError.Create("module.entities.duplicatePhysical", "Module contains duplicate entity physical names."));
        }

        return Result<ModuleModel>.Success(new ModuleModel(name, isSystemModule, isActive, materialized));
    }

    private static bool HasDuplicates(IEnumerable<string> values, IEqualityComparer<string>? comparer = null)
    {
        comparer ??= StringComparer.Ordinal;
        var set = new HashSet<string>(comparer);
        foreach (var value in values)
        {
            if (!set.Add(value))
            {
                return true;
            }
        }

        return false;
    }
}

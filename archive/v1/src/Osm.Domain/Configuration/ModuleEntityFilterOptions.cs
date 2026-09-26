using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;

namespace Osm.Domain.Configuration;

public sealed record ModuleEntityFilterOptions
{
    private ModuleEntityFilterOptions(ImmutableArray<string> names)
    {
        Names = names;
        NameSet = names.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public ImmutableArray<string> Names { get; }

    private ImmutableHashSet<string> NameSet { get; }

    public static Result<ModuleEntityFilterOptions> Create(IEnumerable<string> entityNames)
    {
        if (entityNames is null)
        {
            return ValidationError.Create(
                "moduleFilter.entities.null",
                "Entity filter must not be null.");
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        var errors = ImmutableArray.CreateBuilder<ValidationError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var candidate in entityNames)
        {
            if (candidate is null)
            {
                errors.Add(ValidationError.Create(
                    "moduleFilter.entities.nullEntry",
                    $"Entity name at position {index} must not be null."));
                index++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate))
            {
                errors.Add(ValidationError.Create(
                    "moduleFilter.entities.empty",
                    $"Entity name at position {index} must not be empty or whitespace."));
                index++;
                continue;
            }

            var trimmed = candidate.Trim();
            if (seen.Add(trimmed))
            {
                builder.Add(trimmed);
            }

            index++;
        }

        if (errors.Count > 0)
        {
            return Result<ModuleEntityFilterOptions>.Failure(errors.ToImmutable());
        }

        if (builder.Count == 0)
        {
            return ValidationError.Create(
                "moduleFilter.entities.empty",
                "Entity filter must include at least one entity name.");
        }

        return new ModuleEntityFilterOptions(builder.ToImmutable());
    }

    public bool Matches(EntityModel entity)
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        return NameSet.Contains(entity.LogicalName.Value) || NameSet.Contains(entity.PhysicalName.Value);
    }

    public ImmutableArray<string> GetMissingNames(IEnumerable<EntityModel> entities)
    {
        if (entities is null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
        {
            if (entity is null)
            {
                continue;
            }

            if (NameSet.Contains(entity.LogicalName.Value))
            {
                matched.Add(entity.LogicalName.Value);
            }

            if (NameSet.Contains(entity.PhysicalName.Value))
            {
                matched.Add(entity.PhysicalName.Value);
            }
        }

        if (matched.Count == Names.Length)
        {
            return ImmutableArray<string>.Empty;
        }

        var missing = ImmutableArray.CreateBuilder<string>();
        foreach (var name in Names)
        {
            if (!matched.Contains(name))
            {
                missing.Add(name);
            }
        }

        return missing.ToImmutable();
    }
}

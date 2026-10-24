using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Configuration;

public sealed record NullabilityOverrideRule(ModuleName Module, EntityName Entity, AttributeName Attribute)
{
    public static Result<NullabilityOverrideRule> Create(string? module, string? entity, string? attribute)
    {
        var errors = ImmutableArray.CreateBuilder<ValidationError>();

        var moduleResult = ModuleName.Create(module);
        if (moduleResult.IsFailure)
        {
            errors.AddRange(moduleResult.Errors);
        }

        var entityResult = EntityName.Create(entity);
        if (entityResult.IsFailure)
        {
            errors.AddRange(entityResult.Errors);
        }

        var attributeResult = AttributeName.Create(attribute);
        if (attributeResult.IsFailure)
        {
            errors.AddRange(attributeResult.Errors);
        }

        if (errors.Count > 0)
        {
            return Result<NullabilityOverrideRule>.Failure(errors.ToImmutable());
        }

        return new NullabilityOverrideRule(moduleResult.Value, entityResult.Value, attributeResult.Value);
    }
}

public sealed class NullabilityOverrideOptions
{
    private readonly ImmutableDictionary<string, ImmutableDictionary<string, ImmutableHashSet<string>>> _overrides;

    private NullabilityOverrideOptions(ImmutableDictionary<string, ImmutableDictionary<string, ImmutableHashSet<string>>> overrides)
    {
        _overrides = overrides;
    }

    public static NullabilityOverrideOptions Empty { get; }
        = new(ImmutableDictionary.Create<string, ImmutableDictionary<string, ImmutableHashSet<string>>>(StringComparer.OrdinalIgnoreCase));

    public bool IsEmpty => _overrides.Count == 0;

    public static Result<NullabilityOverrideOptions> Create(IEnumerable<NullabilityOverrideRule>? rules)
    {
        if (rules is null)
        {
            return Result<NullabilityOverrideOptions>.Success(Empty);
        }

        var materialized = rules.ToArray();
        if (materialized.Length == 0)
        {
            return Result<NullabilityOverrideOptions>.Success(Empty);
        }

        var moduleBuilder = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in materialized)
        {
            if (!moduleBuilder.TryGetValue(rule.Module.Value, out var entityMap))
            {
                entityMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                moduleBuilder[rule.Module.Value] = entityMap;
            }

            if (!entityMap.TryGetValue(rule.Entity.Value, out var attributeSet))
            {
                attributeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                entityMap[rule.Entity.Value] = attributeSet;
            }

            attributeSet.Add(rule.Attribute.Value);
        }

        var immutableModules = moduleBuilder.ToImmutableDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToImmutableDictionary(
                static entityPair => entityPair.Key,
                static entityPair => entityPair.Value.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        return Result<NullabilityOverrideOptions>.Success(new NullabilityOverrideOptions(immutableModules));
    }

    public bool ShouldRelax(ModuleName module, EntityName entity, AttributeName attribute)
        => ShouldRelax(module.Value, entity.Value, attribute.Value);

    public bool ShouldRelax(string module, string entity, string attribute)
    {
        if (string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(entity) || string.IsNullOrWhiteSpace(attribute))
        {
            return false;
        }

        if (!_overrides.TryGetValue(module, out var entityMap))
        {
            return false;
        }

        if (!entityMap.TryGetValue(entity, out var attributes))
        {
            return false;
        }

        return attributes.Contains(attribute);
    }
}

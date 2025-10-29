using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Osm.Smo;

internal sealed record TypeMappingPolicyDefinition(
    TypeMappingRuleDefinition Default,
    IReadOnlyDictionary<string, TypeMappingRuleDefinition> AttributeMappings,
    IReadOnlyDictionary<string, TypeMappingRuleDefinition> OnDiskMappings,
    IReadOnlyDictionary<string, TypeMappingRuleDefinition> ExternalMappings)
{
    public static TypeMappingPolicyDefinition Parse(Stream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        TypeMappingRuleDefinition defaultRule;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("default", out var defaultElement))
        {
            if (!TypeMappingRuleDefinition.TryParse(defaultElement, out defaultRule, out var error))
            {
                throw new InvalidOperationException($"Failed to parse default type mapping: {error}");
            }
        }
        else
        {
            defaultRule = new TypeMappingRuleDefinition(TypeMappingStrategy.Fixed, "nvarchar(max)", null, null, null, null, null);
        }

        var attributeMappings = ParseSection(root, "mappings");
        var onDiskMappings = ParseSection(root, "onDisk");
        var externalMappings = ParseSection(root, "external");

        return new TypeMappingPolicyDefinition(defaultRule, attributeMappings, onDiskMappings, externalMappings);
    }

    private static IReadOnlyDictionary<string, TypeMappingRuleDefinition> ParseSection(JsonElement root, string propertyName)
    {
        var mappings = new Dictionary<string, TypeMappingRuleDefinition>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var section) &&
            section.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in section.EnumerateObject())
            {
                if (!TypeMappingRuleDefinition.TryParse(property.Value, out var rule, out var error))
                {
                    throw new InvalidOperationException(
                        $"Failed to parse type mapping for '{property.Name}' in '{propertyName}': {error}");
                }

                var key = TypeMappingKeyNormalizer.Normalize(property.Name);
                mappings[key] = rule;
            }
        }

        return mappings;
    }

    public TypeMappingPolicyDefinition WithOverrides(IReadOnlyDictionary<string, TypeMappingRuleDefinition> overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return this;
        }

        var builder = new Dictionary<string, TypeMappingRuleDefinition>(AttributeMappings, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in overrides)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var key = TypeMappingKeyNormalizer.Normalize(pair.Key);
            builder[key] = pair.Value;
        }

        return this with { AttributeMappings = builder };
    }
}

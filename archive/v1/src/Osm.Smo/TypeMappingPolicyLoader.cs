using System;
using System.Collections.Generic;
using System.IO;

namespace Osm.Smo;

public static class TypeMappingPolicyLoader
{
    private const string DefaultResourceName = "Osm.Smo.Resources.type-mapping.default.json";

    public static TypeMappingPolicy LoadDefault(
        TypeMappingRuleDefinition? defaultOverride = null,
        IReadOnlyDictionary<string, TypeMappingRuleDefinition>? overrides = null)
    {
        var assembly = typeof(TypeMappingPolicyLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(DefaultResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException("Embedded type mapping resource was not found.");
        }

        return Load(stream, defaultOverride, overrides);
    }

    public static TypeMappingPolicy LoadFromFile(
        string path,
        TypeMappingRuleDefinition? defaultOverride = null,
        IReadOnlyDictionary<string, TypeMappingRuleDefinition>? overrides = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Type mapping path must be provided.", nameof(path));
        }

        using var stream = File.OpenRead(path);
        return Load(stream, defaultOverride, overrides);
    }

    internal static TypeMappingPolicy Load(
        Stream jsonStream,
        TypeMappingRuleDefinition? defaultOverride,
        IReadOnlyDictionary<string, TypeMappingRuleDefinition>? overrides)
    {
        if (jsonStream is null)
        {
            throw new ArgumentNullException(nameof(jsonStream));
        }

        var definition = TypeMappingPolicyDefinition.Parse(jsonStream);
        if (defaultOverride is not null)
        {
            definition = definition with { Default = defaultOverride };
        }

        if (overrides is not null && overrides.Count > 0)
        {
            definition = definition.WithOverrides(overrides);
        }

        return CreatePolicy(definition);
    }

    private static TypeMappingPolicy CreatePolicy(TypeMappingPolicyDefinition definition)
    {
        var externalCompiled = CompileRules(definition.ExternalMappings, TypeResolutionSource.External);
        var onDiskCompiled = CompileRules(definition.OnDiskMappings, TypeResolutionSource.OnDisk);
        var attributeCompiled = CompileRules(definition.AttributeMappings, TypeResolutionSource.Attribute);
        var defaultRule = new TypeMappingRule(definition.Default, TypeResolutionSource.Attribute);
        return new TypeMappingPolicy(attributeCompiled, defaultRule, onDiskCompiled, externalCompiled);
    }

    private static IReadOnlyDictionary<string, TypeMappingRule> CompileRules(
        IReadOnlyDictionary<string, TypeMappingRuleDefinition> source,
        TypeResolutionSource resolutionSource)
    {
        var compiled = new Dictionary<string, TypeMappingRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            var key = TypeMappingKeyNormalizer.NormalizeKey(pair.Key);
            compiled[key] = new TypeMappingRule(pair.Value, resolutionSource);
        }

        return compiled;
    }
}

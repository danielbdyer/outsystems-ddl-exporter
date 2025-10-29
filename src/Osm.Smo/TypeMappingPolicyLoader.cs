using System;
using System.Collections.Generic;
using System.IO;

namespace Osm.Smo;

public sealed class TypeMappingPolicyLoader
{
    private const string EmbeddedResourceName = "Osm.Smo.Resources.type-mapping.default.json";

    private static readonly Lazy<TypeMappingPolicy> DefaultPolicyInstance =
        new(() => LoadDefault());

    private readonly TypeMappingPolicyDefinition _definition;

    private TypeMappingPolicyLoader(TypeMappingPolicyDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public static TypeMappingPolicy DefaultPolicy => DefaultPolicyInstance.Value;

    public static TypeMappingPolicy LoadDefault(
        TypeMappingRuleDefinition? defaultOverride = null,
        IReadOnlyDictionary<string, TypeMappingRuleDefinition>? overrides = null)
    {
        return FromEmbeddedResource().CreatePolicy(defaultOverride, overrides);
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
        return LoadFromStream(stream, defaultOverride, overrides);
    }

    internal static TypeMappingPolicy LoadFromStream(
        Stream jsonStream,
        TypeMappingRuleDefinition? defaultOverride = null,
        IReadOnlyDictionary<string, TypeMappingRuleDefinition>? overrides = null)
    {
        if (jsonStream is null)
        {
            throw new ArgumentNullException(nameof(jsonStream));
        }

        return FromStream(jsonStream).CreatePolicy(defaultOverride, overrides);
    }

    public TypeMappingPolicy CreatePolicy(
        TypeMappingRuleDefinition? defaultOverride = null,
        IReadOnlyDictionary<string, TypeMappingRuleDefinition>? overrides = null)
    {
        var definition = _definition;
        if (defaultOverride is not null)
        {
            definition = definition with { Default = defaultOverride };
        }

        if (overrides is not null && overrides.Count > 0)
        {
            definition = definition.WithOverrides(overrides);
        }

        return Compile(definition);
    }

    private static TypeMappingPolicyLoader FromEmbeddedResource()
    {
        using var stream = typeof(TypeMappingPolicyLoader).Assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException("Embedded type mapping resource was not found.");
        }

        return FromStream(stream);
    }

    private static TypeMappingPolicyLoader FromStream(Stream stream)
    {
        var definition = TypeMappingPolicyDefinition.Parse(stream);
        return new TypeMappingPolicyLoader(definition);
    }

    private static TypeMappingPolicy Compile(TypeMappingPolicyDefinition definition)
    {
        var external = CompileRules(definition.ExternalMappings, TypeMappingPolicy.TypeResolutionSource.External);
        var onDisk = CompileRules(definition.OnDiskMappings, TypeMappingPolicy.TypeResolutionSource.OnDisk);
        var attribute = CompileRules(definition.AttributeMappings, TypeMappingPolicy.TypeResolutionSource.Attribute);
        var defaultRule = new TypeMappingPolicy.TypeMappingRule(definition.Default, TypeMappingPolicy.TypeResolutionSource.Attribute);

        return new TypeMappingPolicy(attribute, defaultRule, onDisk, external);
    }

    private static IReadOnlyDictionary<string, TypeMappingPolicy.TypeMappingRule> CompileRules(
        IReadOnlyDictionary<string, TypeMappingRuleDefinition> source,
        TypeMappingPolicy.TypeResolutionSource resolutionSource)
    {
        var compiled = new Dictionary<string, TypeMappingPolicy.TypeMappingRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            var key = TypeMappingKeyNormalizer.Normalize(pair.Key);
            compiled[key] = new TypeMappingPolicy.TypeMappingRule(pair.Value, resolutionSource);
        }

        return compiled;
    }
}

using System;
using System.Collections.Generic;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;

namespace Osm.Smo;

public sealed class TypeMappingPolicy
{
    private static readonly Lazy<TypeMappingPolicy> DefaultInstance = new(() => TypeMappingPolicyLoader.LoadDefault());

    private readonly IReadOnlyDictionary<string, TypeMappingRule> _attributeRules;
    private readonly IReadOnlyDictionary<string, TypeMappingRule> _onDiskRules;
    private readonly IReadOnlyDictionary<string, TypeMappingRule> _externalRules;
    private readonly TypeMappingRule _defaultRule;

    internal TypeMappingPolicy(
        IReadOnlyDictionary<string, TypeMappingRule> attributeRules,
        TypeMappingRule defaultRule,
        IReadOnlyDictionary<string, TypeMappingRule> onDiskRules,
        IReadOnlyDictionary<string, TypeMappingRule> externalRules)
    {
        _attributeRules = attributeRules ?? throw new ArgumentNullException(nameof(attributeRules));
        _defaultRule = defaultRule ?? throw new ArgumentNullException(nameof(defaultRule));
        _onDiskRules = onDiskRules ?? throw new ArgumentNullException(nameof(onDiskRules));
        _externalRules = externalRules ?? throw new ArgumentNullException(nameof(externalRules));
    }

    public static TypeMappingPolicy Default => DefaultInstance.Value;

    public DataType Resolve(AttributeModel attribute)
    {
        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        var normalized = TypeMappingKeyNormalizer.NormalizeKey(attribute.DataType);
        var preferRuntimeMapping = ShouldPreferRuntimeMapping(attribute, normalized);

        if (!preferRuntimeMapping && TryResolveFromOnDisk(attribute, attribute.OnDisk, out var onDiskType)
            && ShouldUseOnDisk(normalized, onDiskType))
        {
            return onDiskType;
        }

        if (!preferRuntimeMapping && !string.IsNullOrWhiteSpace(attribute.ExternalDatabaseType))
        {
            return ResolveExternal(attribute, attribute.ExternalDatabaseType!);
        }

        if (!_attributeRules.TryGetValue(normalized, out var rule))
        {
            rule = _defaultRule;
        }

        return rule.Apply(TypeMappingRequest.ForAttribute(attribute));
    }

    private static bool ShouldUseOnDisk(string normalizedDataType, DataType onDiskType)
    {
        if (normalizedDataType == "date" && onDiskType.SqlDataType != SqlDataType.Date)
        {
            return false;
        }

        return true;
    }

    private bool TryResolveFromOnDisk(AttributeModel attribute, AttributeOnDiskMetadata? onDisk, out DataType dataType)
    {
        dataType = DataType.NVarCharMax;
        if (onDisk is null || string.IsNullOrWhiteSpace(onDisk.SqlType))
        {
            return false;
        }

        var key = TypeMappingKeyNormalizer.NormalizeKey(onDisk.SqlType);
        if (!_onDiskRules.TryGetValue(key, out var rule))
        {
            return false;
        }

        dataType = rule.Apply(TypeMappingRequest.ForOnDisk(attribute, onDisk));
        return true;
    }

    private DataType ResolveExternal(AttributeModel attribute, string externalType)
    {
        var (baseType, parameters) = ExternalDatabaseTypeParser.Parse(externalType);
        var key = TypeMappingKeyNormalizer.NormalizeKey(baseType);

        if (_externalRules.TryGetValue(key, out var rule))
        {
            return rule.Apply(TypeMappingRequest.ForExternal(attribute, parameters));
        }

        return _defaultRule.Apply(TypeMappingRequest.ForExternal(attribute, parameters));
    }

    private static bool ShouldPreferRuntimeMapping(AttributeModel attribute, string normalizedDataType)
    {
        if (attribute.IsIdentifier || attribute.IsAutoNumber)
        {
            return true;
        }

        return normalizedDataType is "identifier" or "autonumber" or "longinteger";
    }
}

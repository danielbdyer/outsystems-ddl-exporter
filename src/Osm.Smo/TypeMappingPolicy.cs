using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;

namespace Osm.Smo;

public sealed class TypeMappingPolicy
{
    private const int DefaultUnicodeMaxLengthThreshold = 2000;
    private const int DefaultVarBinaryMaxLengthThreshold = 2000;

    private static readonly Lazy<TypeMappingPolicy> DefaultInstance = new(() => LoadDefault());

    private readonly IReadOnlyDictionary<string, TypeMappingRule> _attributeRules;
    private readonly IReadOnlyDictionary<string, TypeMappingRule> _onDiskRules;
    private readonly IReadOnlyDictionary<string, TypeMappingRule> _externalRules;
    private readonly TypeMappingRule _defaultRule;

    private TypeMappingPolicy(
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

    public static TypeMappingPolicy LoadDefault(
        TypeMappingRuleDefinition? defaultOverride = null,
        IReadOnlyDictionary<string, TypeMappingRuleDefinition>? overrides = null)
    {
        using var stream = typeof(TypeMappingPolicy).Assembly.GetManifestResourceStream("Osm.Smo.Resources.type-mapping.default.json");
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

        return definition.ToPolicy();
    }

    public DataType Resolve(AttributeModel attribute)
    {
        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        var normalized = NormalizeKey(attribute.DataType);
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

    internal static string NormalizeKey(string? dataType) => NormalizeDataType(dataType ?? string.Empty);

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

        var key = NormalizeKey(onDisk.SqlType);
        if (!_onDiskRules.TryGetValue(key, out var rule))
        {
            return false;
        }

        dataType = rule.Apply(TypeMappingRequest.ForOnDisk(attribute, onDisk));
        return true;
    }

    private DataType ResolveExternal(AttributeModel attribute, string externalType)
    {
        var (baseType, parameters) = ParseExternal(externalType);
        var key = NormalizeKey(baseType);

        if (_externalRules.TryGetValue(key, out var rule))
        {
            return rule.Apply(TypeMappingRequest.ForExternal(attribute, parameters));
        }

        return _defaultRule.Apply(TypeMappingRequest.ForExternal(attribute, parameters));
    }

    private static (string BaseType, IReadOnlyList<int> Parameters) ParseExternal(string externalType)
    {
        var trimmed = externalType.Trim();
        var openParen = trimmed.IndexOf('(');
        if (openParen < 0)
        {
            return (trimmed, Array.Empty<int>());
        }

        var baseType = trimmed[..openParen].Trim();
        var closeParen = trimmed.IndexOf(')', openParen + 1);
        var argsSegment = closeParen > openParen
            ? trimmed[(openParen + 1)..closeParen]
            : trimmed[(openParen + 1)..];

        var parts = argsSegment
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseExternalParameter)
            .ToArray();

        return (baseType, parts);
    }

    private static DataType ResolveUnicodeText(int? length, int? fallbackLength, int maxThreshold)
    {
        var effectiveLength = length ?? fallbackLength;
        if (effectiveLength is null or <= 0)
        {
            return DataType.NVarCharMax;
        }

        if (effectiveLength == -1 || effectiveLength >= maxThreshold)
        {
            return DataType.NVarCharMax;
        }

        return DataType.NVarChar(effectiveLength.Value);
    }

    private static DataType ResolveVarChar(int? length)
    {
        if (length is null or <= 0)
        {
            return DataType.VarCharMax;
        }

        return length == -1 ? DataType.VarCharMax : DataType.VarChar(length.Value);
    }

    private static DataType ResolveVarCharText(int? length, int fallbackLength)
    {
        var effectiveLength = length ?? fallbackLength;
        if (effectiveLength <= 0)
        {
            effectiveLength = fallbackLength;
        }

        return ResolveVarChar(effectiveLength);
    }

    private static DataType ResolveVarBinary(int? length, int maxThreshold)
    {
        if (length is null or <= 0)
        {
            return DataType.VarBinaryMax;
        }

        var resolvedLength = length.Value;
        if (resolvedLength == -1 || resolvedLength >= maxThreshold)
        {
            return DataType.VarBinaryMax;
        }

        return DataType.VarBinary(resolvedLength);
    }

    private static DataType ResolveDecimal(int? precision, int? scale, int defaultPrecision, int defaultScale)
    {
        var resolvedPrecision = ResolvePrecision(precision, defaultPrecision);
        var resolvedScale = ResolveScaleValue(scale, defaultScale);
        return DataType.Decimal(resolvedPrecision, resolvedScale);
    }

    private static string NormalizeDataType(string dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return string.Empty;
        }

        var trimmed = dataType.Trim();
        if (trimmed.StartsWith("rt", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 2)
        {
            trimmed = trimmed[2..];
        }

        trimmed = trimmed
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return trimmed.ToLowerInvariant();
    }

    private static int ResolvePrecision(int? value, int defaultValue)
    {
        if (value is not null && value > 0)
        {
            return value.Value;
        }

        return defaultValue;
    }

    private static int ResolveScaleValue(int? value, int defaultValue)
    {
        if (value is not null && value >= 0)
        {
            return value.Value;
        }

        return defaultValue;
    }

    private static bool ShouldPreferRuntimeMapping(AttributeModel attribute, string normalizedDataType)
    {
        if (attribute.IsIdentifier || attribute.IsAutoNumber)
        {
            return true;
        }

        return normalizedDataType is "identifier" or "autonumber" or "longinteger";
    }

    private static int ParseExternalParameter(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return 0;
        }

        var trimmed = segment.Trim();
        if (trimmed.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        return int.TryParse(trimmed, out var value) ? value : 0;
    }

    private static DataType ResolveFixedSqlType(string? sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType))
        {
            return DataType.NVarCharMax;
        }

        var (baseType, parameters) = ParseExternal(sqlType);
        var key = NormalizeKey(baseType);

        if (FixedTypeResolvers.TryGetValue(key, out var resolver))
        {
            return resolver(parameters);
        }

        return DataType.NVarCharMax;
    }

    private static readonly IReadOnlyDictionary<string, Func<IReadOnlyList<int>, DataType>> FixedTypeResolvers =
        new Dictionary<string, Func<IReadOnlyList<int>, DataType>>(StringComparer.OrdinalIgnoreCase)
        {
            ["varchar"] = parameters => ResolveVarChar(parameters.Count > 0 ? parameters[0] : (int?)null),
            ["nvarchar"] = parameters => ResolveUnicodeText(parameters.Count > 0 ? parameters[0] : (int?)null, null, DefaultUnicodeMaxLengthThreshold),
            ["nchar"] = parameters => DataType.NChar(Math.Max(1, parameters.Count > 0 ? NormalizeFixedLength(parameters[0]) : 1)),
            ["char"] = parameters => DataType.Char(Math.Max(1, parameters.Count > 0 ? NormalizeFixedLength(parameters[0]) : 1)),
            ["varbinary"] = parameters => ResolveVarBinary(parameters.Count > 0 ? parameters[0] : (int?)null, DefaultVarBinaryMaxLengthThreshold),
            ["binary"] = parameters => DataType.Binary(Math.Max(1, parameters.Count > 0 ? NormalizeFixedLength(parameters[0]) : 1)),
            ["decimal"] = parameters => ResolveDecimal(
                parameters.Count > 0 ? parameters[0] : (int?)null,
                parameters.Count > 1 ? parameters[1] : (int?)null,
                18,
                0),
            ["numeric"] = parameters => ResolveDecimal(
                parameters.Count > 0 ? parameters[0] : (int?)null,
                parameters.Count > 1 ? parameters[1] : (int?)null,
                18,
                0),
            ["int"] = _ => DataType.Int,
            ["bigint"] = _ => DataType.BigInt,
            ["smallint"] = _ => DataType.SmallInt,
            ["tinyint"] = _ => DataType.TinyInt,
            ["bit"] = _ => DataType.Bit,
            ["datetime"] = _ => DataType.DateTime,
            ["datetime2"] = parameters => DataType.DateTime2(ResolveScaleValue(parameters.Count > 0 ? parameters[0] : null, 7)),
            ["datetimeoffset"] = parameters => DataType.DateTimeOffset(ResolveScaleValue(parameters.Count > 0 ? parameters[0] : null, 7)),
            ["smalldatetime"] = _ => DataType.SmallDateTime,
            ["date"] = _ => DataType.Date,
            ["time"] = parameters => DataType.Time(ResolveScaleValue(parameters.Count > 0 ? parameters[0] : null, 7)),
            ["uniqueidentifier"] = _ => DataType.UniqueIdentifier,
            ["float"] = _ => DataType.Float,
            ["real"] = _ => DataType.Real,
            ["money"] = _ => DataType.Money,
            ["smallmoney"] = _ => DataType.SmallMoney,
            ["xml"] = _ => new DataType(SqlDataType.Xml),
            ["text"] = _ => DataType.Text,
            ["ntext"] = _ => DataType.NText,
            ["image"] = _ => DataType.Image,
        };

    private static int NormalizeFixedLength(int value)
        => value <= 0 ? 1 : value;

    private enum TypeResolutionSource
    {
        Attribute,
        OnDisk,
        External,
    }

    private sealed class TypeMappingRequest
    {
        private TypeMappingRequest(AttributeModel attribute, AttributeOnDiskMetadata? onDisk, IReadOnlyList<int> parameters, TypeResolutionSource source)
        {
            Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
            OnDisk = onDisk;
            Parameters = parameters ?? Array.Empty<int>();
            Source = source;
        }

        public AttributeModel Attribute { get; }

        public AttributeOnDiskMetadata? OnDisk { get; }

        public IReadOnlyList<int> Parameters { get; }

        public TypeResolutionSource Source { get; }

        public static TypeMappingRequest ForAttribute(AttributeModel attribute)
            => new(attribute, attribute.OnDisk, Array.Empty<int>(), TypeResolutionSource.Attribute);

        public static TypeMappingRequest ForOnDisk(AttributeModel attribute, AttributeOnDiskMetadata onDisk)
            => new(attribute, onDisk, Array.Empty<int>(), TypeResolutionSource.OnDisk);

        public static TypeMappingRequest ForExternal(AttributeModel attribute, IReadOnlyList<int> parameters)
            => new(attribute, attribute.OnDisk, parameters, TypeResolutionSource.External);

        public int? GetParameter(int index)
        {
            if (index < 0 || Parameters.Count <= index)
            {
                return null;
            }

            return Parameters[index];
        }
    }

    private sealed class TypeMappingRule
    {
        private readonly TypeMappingStrategy _strategy;
        private readonly string? _sqlType;
        private readonly int? _fallbackLength;
        private readonly int? _defaultPrecision;
        private readonly int? _defaultScale;
        private readonly int? _scale;
        private readonly int? _maxLengthThreshold;
        private readonly TypeValueSource _lengthSource;
        private readonly TypeValueSource _precisionSource;
        private readonly TypeValueSource _scaleSource;
        private readonly int? _lengthParameterIndex;
        private readonly int? _precisionParameterIndex;
        private readonly int? _scaleParameterIndex;

        public TypeMappingRule(TypeMappingRuleDefinition definition, TypeResolutionSource source)
        {
            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            _strategy = definition.Strategy;
            _sqlType = string.IsNullOrWhiteSpace(definition.SqlType) ? null : definition.SqlType!.Trim();
            _fallbackLength = definition.FallbackLength;
            _defaultPrecision = definition.DefaultPrecision;
            _defaultScale = definition.DefaultScale;
            _scale = definition.Scale;
            _maxLengthThreshold = definition.MaxLengthThreshold;
            _lengthSource = definition.LengthSource ?? GetDefaultLengthSource(source);
            _precisionSource = definition.PrecisionSource ?? GetDefaultPrecisionSource(source);
            _scaleSource = definition.ScaleSource ?? GetDefaultScaleSource(source);
            _lengthParameterIndex = definition.LengthParameterIndex;
            _precisionParameterIndex = definition.PrecisionParameterIndex;
            _scaleParameterIndex = definition.ScaleParameterIndex;
        }

        public DataType Apply(TypeMappingRequest request)
        {
            return _strategy switch
            {
                TypeMappingStrategy.Fixed => ResolveFixedSqlType(_sqlType),
                TypeMappingStrategy.UnicodeText => ResolveUnicodeText(
                    GetLength(request),
                    _fallbackLength,
                    _maxLengthThreshold ?? DefaultUnicodeMaxLengthThreshold),
                TypeMappingStrategy.VarChar => ResolveVarChar(GetLength(request) ?? _fallbackLength),
                TypeMappingStrategy.VarCharText => ResolveVarCharText(GetLength(request), _fallbackLength ?? 0),
                TypeMappingStrategy.VarBinary => ResolveVarBinary(GetLength(request) ?? _fallbackLength, _maxLengthThreshold ?? DefaultVarBinaryMaxLengthThreshold),
                TypeMappingStrategy.Decimal => ResolveDecimal(
                    GetPrecision(request),
                    GetScale(request),
                    _defaultPrecision ?? 18,
                    _defaultScale ?? 0),
                TypeMappingStrategy.DateTime2 => DataType.DateTime2(ResolveScaleValue(GetScale(request), _scale ?? 7)),
                TypeMappingStrategy.DateTimeOffset => DataType.DateTimeOffset(ResolveScaleValue(GetScale(request), _scale ?? 7)),
                TypeMappingStrategy.Time => DataType.Time(ResolveScaleValue(GetScale(request), _scale ?? 7)),
                TypeMappingStrategy.NChar => DataType.NChar(Math.Max(1, GetLength(request) ?? _fallbackLength ?? 1)),
                TypeMappingStrategy.Char => DataType.Char(Math.Max(1, GetLength(request) ?? _fallbackLength ?? 1)),
                TypeMappingStrategy.Binary => DataType.Binary(Math.Max(1, GetLength(request) ?? _fallbackLength ?? 1)),
                _ => ResolveFixedSqlType(_sqlType),
            };
        }

        private int? GetLength(TypeMappingRequest request)
        {
            return _lengthSource switch
            {
                TypeValueSource.Attribute => request.Attribute.Length,
                TypeValueSource.OnDisk => request.OnDisk?.MaxLength,
                TypeValueSource.OnDiskOrAttribute => request.OnDisk?.MaxLength ?? request.Attribute.Length,
                TypeValueSource.Parameters => request.GetParameter(_lengthParameterIndex ?? 0),
                _ => request.Attribute.Length,
            };
        }

        private int? GetPrecision(TypeMappingRequest request)
        {
            return _precisionSource switch
            {
                TypeValueSource.Attribute => request.Attribute.Precision,
                TypeValueSource.OnDisk => request.OnDisk?.Precision,
                TypeValueSource.OnDiskOrAttribute => request.OnDisk?.Precision ?? request.Attribute.Precision,
                TypeValueSource.Parameters => request.GetParameter(_precisionParameterIndex ?? 0),
                _ => request.Attribute.Precision,
            };
        }

        private int? GetScale(TypeMappingRequest request)
        {
            return _scaleSource switch
            {
                TypeValueSource.Attribute => request.Attribute.Scale,
                TypeValueSource.OnDisk => request.OnDisk?.Scale,
                TypeValueSource.OnDiskOrAttribute => request.OnDisk?.Scale ?? request.Attribute.Scale,
                TypeValueSource.Parameters => request.GetParameter(_scaleParameterIndex ?? 0),
                _ => request.Attribute.Scale,
            };
        }

        private static TypeValueSource GetDefaultLengthSource(TypeResolutionSource source)
        {
            return source switch
            {
                TypeResolutionSource.Attribute => TypeValueSource.Attribute,
                TypeResolutionSource.OnDisk => TypeValueSource.OnDiskOrAttribute,
                TypeResolutionSource.External => TypeValueSource.Parameters,
                _ => TypeValueSource.Attribute,
            };
        }

        private static TypeValueSource GetDefaultPrecisionSource(TypeResolutionSource source)
        {
            return source switch
            {
                TypeResolutionSource.Attribute => TypeValueSource.Attribute,
                TypeResolutionSource.OnDisk => TypeValueSource.OnDiskOrAttribute,
                TypeResolutionSource.External => TypeValueSource.Parameters,
                _ => TypeValueSource.Attribute,
            };
        }

        private static TypeValueSource GetDefaultScaleSource(TypeResolutionSource source)
        {
            return source switch
            {
                TypeResolutionSource.Attribute => TypeValueSource.Attribute,
                TypeResolutionSource.OnDisk => TypeValueSource.OnDiskOrAttribute,
                TypeResolutionSource.External => TypeValueSource.Parameters,
                _ => TypeValueSource.Attribute,
            };
        }
    }

    private sealed record TypeMappingPolicyDefinition(
        TypeMappingRuleDefinition Default,
        IReadOnlyDictionary<string, TypeMappingRuleDefinition> AttributeMappings,
        IReadOnlyDictionary<string, TypeMappingRuleDefinition> OnDiskMappings,
        IReadOnlyDictionary<string, TypeMappingRuleDefinition> ExternalMappings)
    {
        public static TypeMappingPolicyDefinition Parse(Stream stream)
        {
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
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out var section) && section.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in section.EnumerateObject())
                {
                    if (!TypeMappingRuleDefinition.TryParse(property.Value, out var rule, out var error))
                    {
                        throw new InvalidOperationException($"Failed to parse type mapping for '{property.Name}' in '{propertyName}': {error}");
                    }

                    var key = NormalizeKey(property.Name);
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

                var key = NormalizeKey(pair.Key);
                builder[key] = pair.Value;
            }

            return this with { AttributeMappings = builder };
        }

        public TypeMappingPolicy ToPolicy()
        {
            var externalCompiled = CompileRules(ExternalMappings, TypeResolutionSource.External);
            var onDiskCompiled = CompileRules(OnDiskMappings, TypeResolutionSource.OnDisk);
            var attributeCompiled = CompileRules(AttributeMappings, TypeResolutionSource.Attribute);
            return new TypeMappingPolicy(attributeCompiled, new TypeMappingRule(Default, TypeResolutionSource.Attribute), onDiskCompiled, externalCompiled);
        }

        private static IReadOnlyDictionary<string, TypeMappingRule> CompileRules(
            IReadOnlyDictionary<string, TypeMappingRuleDefinition> source,
            TypeResolutionSource resolutionSource)
        {
            var compiled = new Dictionary<string, TypeMappingRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in source)
            {
                var key = NormalizeKey(pair.Key);
                compiled[key] = new TypeMappingRule(pair.Value, resolutionSource);
            }

            return compiled;
        }
    }
}

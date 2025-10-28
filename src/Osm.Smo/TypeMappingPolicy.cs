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

    private readonly IReadOnlyDictionary<string, TypeMappingRule> _rules;
    private readonly TypeMappingRule _defaultRule;

    private TypeMappingPolicy(IReadOnlyDictionary<string, TypeMappingRule> rules, TypeMappingRule defaultRule)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _defaultRule = defaultRule ?? throw new ArgumentNullException(nameof(defaultRule));
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
            return ResolveExternal(attribute.ExternalDatabaseType!);
        }

        if (!_rules.TryGetValue(normalized, out var rule))
        {
            rule = _defaultRule;
        }

        return rule.Apply(attribute);
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

    private static bool TryResolveFromOnDisk(AttributeModel attribute, AttributeOnDiskMetadata? onDisk, out DataType dataType)
    {
        dataType = DataType.NVarCharMax;
        if (onDisk is null || string.IsNullOrWhiteSpace(onDisk.SqlType))
        {
            return false;
        }

        var sqlType = onDisk.SqlType!.Trim().ToLowerInvariant();
        switch (sqlType)
        {
            case "nvarchar":
                dataType = ResolveUnicodeText(onDisk.MaxLength, attribute.Length, DefaultUnicodeMaxLengthThreshold);
                return true;
            case "nchar":
                var ncharLength = onDisk.MaxLength ?? attribute.Length ?? 1;
                dataType = DataType.NChar(Math.Max(1, ncharLength));
                return true;
            case "varchar":
                dataType = ResolveVarChar(onDisk.MaxLength ?? attribute.Length ?? 0);
                return true;
            case "char":
                var charLength = onDisk.MaxLength ?? attribute.Length ?? 1;
                dataType = DataType.Char(Math.Max(1, charLength));
                return true;
            case "varbinary":
                dataType = ResolveVarBinary(onDisk.MaxLength ?? attribute.Length ?? 0);
                return true;
            case "binary":
                var binaryLength = onDisk.MaxLength ?? attribute.Length ?? 1;
                dataType = DataType.Binary(Math.Max(1, binaryLength));
                return true;
            case "decimal":
            case "numeric":
                dataType = DataType.Decimal(
                    ResolvePrecision(onDisk.Precision, attribute.Precision),
                    ResolveScale(onDisk.Scale, attribute.Scale));
                return true;
            case "money":
                dataType = DataType.Money;
                return true;
            case "smallmoney":
                dataType = DataType.SmallMoney;
                return true;
            case "bit":
                dataType = DataType.Bit;
                return true;
            case "bigint":
                dataType = DataType.BigInt;
                return true;
            case "int":
                dataType = DataType.Int;
                return true;
            case "smallint":
                dataType = DataType.SmallInt;
                return true;
            case "tinyint":
                dataType = DataType.TinyInt;
                return true;
            case "datetime":
                dataType = DataType.DateTime;
                return true;
            case "smalldatetime":
                dataType = DataType.SmallDateTime;
                return true;
            case "datetime2":
                dataType = DataType.DateTime2(ResolveScale(onDisk.Scale, attribute.Scale, 7));
                return true;
            case "datetimeoffset":
                dataType = DataType.DateTimeOffset(ResolveScale(onDisk.Scale, attribute.Scale, 7));
                return true;
            case "date":
                dataType = DataType.Date;
                return true;
            case "time":
                dataType = DataType.Time(ResolveScale(onDisk.Scale, attribute.Scale, 7));
                return true;
            case "uniqueidentifier":
                dataType = DataType.UniqueIdentifier;
                return true;
            case "float":
                dataType = DataType.Float;
                return true;
            case "real":
                dataType = DataType.Real;
                return true;
            case "xml":
                dataType = new DataType(SqlDataType.Xml);
                return true;
            case "text":
                dataType = DataType.Text;
                return true;
            case "ntext":
                dataType = DataType.NText;
                return true;
            case "image":
                dataType = DataType.Image;
                return true;
        }

        return false;
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

    private static DataType ResolveVarBinary(int? length)
    {
        if (length is null or <= 0)
        {
            return DataType.VarBinaryMax;
        }

        var resolvedLength = length.Value;
        if (resolvedLength >= DefaultVarBinaryMaxLengthThreshold)
        {
            return DataType.VarBinaryMax;
        }

        return DataType.VarBinary(resolvedLength);
    }

    private static DataType ResolveDecimal(int? precision, int? scale, int defaultPrecision = 18, int defaultScale = 0)
    {
        var resolvedPrecision = precision is null or <= 0 ? defaultPrecision : precision.Value;
        var resolvedScale = scale is null or < 0 ? defaultScale : scale.Value;
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

    private static int ResolvePrecision(int? onDisk, int? attribute, int defaultValue = 18)
    {
        if (onDisk is not null && onDisk > 0)
        {
            return onDisk.Value;
        }

        if (attribute is not null && attribute > 0)
        {
            return attribute.Value;
        }

        return defaultValue;
    }

    private static int ResolveScale(int? onDisk, int? attribute, int defaultValue = 0)
    {
        if (onDisk is not null && onDisk >= 0)
        {
            return onDisk.Value;
        }

        if (attribute is not null && attribute >= 0)
        {
            return attribute.Value;
        }

        return defaultValue;
    }

    private static DataType ResolveExternal(string externalType)
    {
        var trimmed = externalType.Trim();
        var openParen = trimmed.IndexOf('(');
        if (openParen < 0)
        {
            return ResolveExternalCore(trimmed, Array.Empty<int>());
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

        return ResolveExternalCore(baseType, parts);
    }

    private static DataType ResolveExternalCore(string baseType, IReadOnlyList<int> parameters)
    {
        switch (baseType.ToLowerInvariant())
        {
            case "varchar":
                var varcharLength = parameters.Count > 0 ? parameters[0] : 0;
                return ResolveVarChar(varcharLength);
            case "nvarchar":
                var nvarcharLength = parameters.Count > 0 ? parameters[0] : 0;
                return ResolveUnicodeText(nvarcharLength, null, DefaultUnicodeMaxLengthThreshold);
            case "nchar":
                var ncharLength = parameters.Count > 0 ? parameters[0] : 0;
                return DataType.NChar(Math.Max(1, ncharLength <= 0 ? 1 : ncharLength));
            case "char":
                var charLength = parameters.Count > 0 ? parameters[0] : 0;
                return DataType.Char(Math.Max(1, charLength <= 0 ? 1 : charLength));
            case "varbinary":
                var varBinaryLength = parameters.Count > 0 ? parameters[0] : 0;
                return ResolveVarBinary(varBinaryLength);
            case "binary":
                var binaryLength = parameters.Count > 0 ? parameters[0] : 0;
                return DataType.Binary(Math.Max(1, binaryLength <= 0 ? 1 : binaryLength));
            case "decimal":
            case "numeric":
                var precision = parameters.Count > 0 ? parameters[0] : 18;
                var scale = parameters.Count > 1 ? parameters[1] : 0;
                return DataType.Decimal(precision, scale);
            case "int":
                return DataType.Int;
            case "bigint":
                return DataType.BigInt;
            case "smallint":
                return DataType.SmallInt;
            case "tinyint":
                return DataType.TinyInt;
            case "bit":
                return DataType.Bit;
            case "datetime":
                return DataType.DateTime;
            case "datetime2":
                var datetime2Scale = parameters.Count > 0 ? parameters[0] : 7;
                return DataType.DateTime2(Math.Clamp(datetime2Scale, 0, 7));
            case "datetimeoffset":
                var offsetScale = parameters.Count > 0 ? parameters[0] : 7;
                return DataType.DateTimeOffset(Math.Clamp(offsetScale, 0, 7));
            case "smalldatetime":
                return DataType.SmallDateTime;
            case "date":
                return DataType.Date;
            case "time":
                var timeScale = parameters.Count > 0 ? parameters[0] : 7;
                return DataType.Time(Math.Clamp(timeScale, 0, 7));
            case "uniqueidentifier":
                return DataType.UniqueIdentifier;
            case "float":
                return DataType.Float;
            case "real":
                return DataType.Real;
            case "money":
                return DataType.Money;
            case "smallmoney":
                return DataType.SmallMoney;
            case "image":
                return DataType.Image;
            default:
                return DataType.NVarCharMax;
        }
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

    private sealed class TypeMappingRule
    {
        private readonly TypeMappingStrategy _strategy;
        private readonly string? _sqlType;
        private readonly int? _fallbackLength;
        private readonly int? _defaultPrecision;
        private readonly int? _defaultScale;
        private readonly int? _scale;
        private readonly int? _maxLengthThreshold;

        public TypeMappingRule(TypeMappingRuleDefinition definition)
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
        }

        public DataType Apply(AttributeModel attribute)
        {
            return _strategy switch
            {
                TypeMappingStrategy.Fixed => ResolveFixed(_sqlType),
                TypeMappingStrategy.UnicodeText => ResolveUnicodeText(attribute.Length, _fallbackLength, _maxLengthThreshold ?? DefaultUnicodeMaxLengthThreshold),
                TypeMappingStrategy.VarChar => ResolveVarChar(attribute.Length ?? _fallbackLength),
                TypeMappingStrategy.VarCharText => ResolveVarCharText(attribute.Length, _fallbackLength ?? 0),
                TypeMappingStrategy.VarBinary => ResolveVarBinary(attribute.Length ?? _fallbackLength),
                TypeMappingStrategy.Decimal => ResolveDecimal(attribute.Precision, attribute.Scale, _defaultPrecision ?? 18, _defaultScale ?? 0),
                TypeMappingStrategy.DateTime2 => DataType.DateTime2(ResolveScale(null, attribute.Scale, _scale ?? 7)),
                TypeMappingStrategy.DateTimeOffset => DataType.DateTimeOffset(ResolveScale(null, attribute.Scale, _scale ?? 7)),
                TypeMappingStrategy.Time => DataType.Time(ResolveScale(null, attribute.Scale, _scale ?? 7)),
                _ => ResolveFixed(_sqlType),
            };
        }

        private static DataType ResolveFixed(string? sqlType)
        {
            return string.IsNullOrWhiteSpace(sqlType) ? DataType.NVarCharMax : ResolveExternal(sqlType);
        }
    }

    private sealed record TypeMappingPolicyDefinition(
        TypeMappingRuleDefinition Default,
        IReadOnlyDictionary<string, TypeMappingRuleDefinition> Mappings)
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

            var mappings = new Dictionary<string, TypeMappingRuleDefinition>(StringComparer.OrdinalIgnoreCase);
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("mappings", out var mappingsElement) && mappingsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in mappingsElement.EnumerateObject())
                {
                    if (!TypeMappingRuleDefinition.TryParse(property.Value, out var rule, out var error))
                    {
                        throw new InvalidOperationException($"Failed to parse type mapping for '{property.Name}': {error}");
                    }

                    var key = NormalizeKey(property.Name);
                    mappings[key] = rule;
                }
            }

            return new TypeMappingPolicyDefinition(defaultRule, mappings);
        }

        public TypeMappingPolicyDefinition WithOverrides(IReadOnlyDictionary<string, TypeMappingRuleDefinition> overrides)
        {
            if (overrides is null || overrides.Count == 0)
            {
                return this;
            }

            var builder = new Dictionary<string, TypeMappingRuleDefinition>(Mappings, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in overrides)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                var key = NormalizeKey(pair.Key);
                builder[key] = pair.Value;
            }

            return this with { Mappings = builder };
        }

        public TypeMappingPolicy ToPolicy()
        {
            var compiled = new Dictionary<string, TypeMappingRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Mappings)
            {
                var key = NormalizeKey(pair.Key);
                compiled[key] = new TypeMappingRule(pair.Value);
            }

            return new TypeMappingPolicy(compiled, new TypeMappingRule(Default));
        }
    }
}

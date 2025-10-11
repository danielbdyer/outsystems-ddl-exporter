using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;

namespace Osm.Smo.TypeMapping;

public sealed class TypeMappingPolicy
{
    private static readonly Lazy<TypeMappingPolicy> DefaultPolicy = new(LoadEmbeddedDefault);

    private readonly ImmutableDictionary<string, TypeMappingRule> _rules;
    private readonly TypeMappingRule _defaultRule;

    private TypeMappingPolicy(
        ImmutableDictionary<string, TypeMappingRule> rules,
        TypeMappingRule defaultRule)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _defaultRule = defaultRule ?? throw new ArgumentNullException(nameof(defaultRule));
    }

    public static TypeMappingPolicy LoadDefault() => DefaultPolicy.Value;

    public static TypeMappingPolicy LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Type map path must be provided.", nameof(path));
        }

        using var stream = File.OpenRead(path);
        return LoadFromStream(stream);
    }

    public static TypeMappingPolicy LoadFromStream(Stream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var document = JsonSerializer.Deserialize<TypeMappingPolicyDocument>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("Unable to deserialize type mapping policy document.");

        return FromDocument(document);
    }

    public TypeMappingPolicy Merge(TypeMappingPolicy overrides)
    {
        if (overrides is null)
        {
            throw new ArgumentNullException(nameof(overrides));
        }

        if (ReferenceEquals(this, overrides))
        {
            return this;
        }

        var builder = _rules.ToBuilder();
        foreach (var (key, rule) in overrides._rules)
        {
            builder[key] = rule;
        }

        var defaultRule = overrides._defaultRule ?? _defaultRule;
        return new TypeMappingPolicy(builder.ToImmutable(), defaultRule);
    }

    public DataType Resolve(AttributeModel attribute)
    {
        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        var normalized = NormalizeDataType(attribute.DataType);
        if (!_rules.TryGetValue(normalized, out var rule))
        {
            rule = _defaultRule;
        }

        if (rule.Kind == TypeMappingKind.Identifier)
        {
            return ResolveFromRule(attribute, rule);
        }

        if (TryResolveFromOnDisk(attribute, attribute.OnDisk, out var onDiskType))
        {
            return onDiskType;
        }

        if (!string.IsNullOrWhiteSpace(attribute.ExternalDatabaseType))
        {
            return ResolveExternal(attribute.ExternalDatabaseType!);
        }

        return ResolveFromRule(attribute, rule);
    }

    private static TypeMappingPolicy LoadEmbeddedDefault()
    {
        var assembly = typeof(TypeMappingPolicy).GetTypeInfo().Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("default-type-map.json", StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidOperationException("Default type mapping resource not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException("Unable to open embedded type mapping resource stream.");
        }

        return LoadFromStream(stream);
    }

    private static TypeMappingPolicy FromDocument(TypeMappingPolicyDocument document)
    {
        if (document.Rules is null)
        {
            throw new InvalidOperationException("Type mapping document is missing rules.");
        }

        var ruleBuilder = ImmutableDictionary.CreateBuilder<string, TypeMappingRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in document.Rules)
        {
            if (string.IsNullOrWhiteSpace(definition.LogicalType))
            {
                continue;
            }

            var normalized = NormalizeDataType(definition.LogicalType);
            ruleBuilder[normalized] = TypeMappingRule.Create(definition);
        }

        var defaultRule = document.Default is null
            ? TypeMappingRule.CreateDefault()
            : TypeMappingRule.Create(document.Default, allowIdentifier: false);

        return new TypeMappingPolicy(ruleBuilder.ToImmutable(), defaultRule);
    }

    private static DataType ResolveFromRule(AttributeModel attribute, TypeMappingRule rule)
    {
        return rule.Kind switch
        {
            TypeMappingKind.Fixed => rule.FixedType!,
            TypeMappingKind.Unicode => ResolveUnicodeText(attribute.Length, rule.MaxLengthThreshold, rule.FallbackLength),
            TypeMappingKind.VarChar => ResolveVarCharText(attribute.Length, rule.FallbackLength),
            TypeMappingKind.VarBinary => ResolveVarBinary(attribute.Length),
            TypeMappingKind.Decimal => ResolveDecimal(attribute.Precision, attribute.Scale, rule.DefaultPrecision, rule.DefaultScale),
            TypeMappingKind.DateTime => ResolveDateTime(rule.SqlTypeName, attribute.Scale, rule.DefaultScale),
            TypeMappingKind.Identifier => ResolveIdentifier(attribute, rule),
            _ => DataType.NVarCharMax,
        };
    }

    private static DataType ResolveIdentifier(AttributeModel attribute, TypeMappingRule rule)
    {
        var identifier = rule.Identifier ?? TypeMappingIdentifierRule.BigInt;

        if (attribute.IsIdentifier)
        {
            return identifier.PrimaryKeyType;
        }

        if (attribute.Reference.IsReference && string.Equals(attribute.DataType, "Identifier", StringComparison.OrdinalIgnoreCase))
        {
            return identifier.ForeignKeyType ?? identifier.PrimaryKeyType;
        }

        return identifier.DefaultType ?? identifier.PrimaryKeyType;
    }

    private static DataType ResolveUnicodeText(int? length, int? maxThreshold, int? fallbackLength)
    {
        var effectiveLength = length ?? fallbackLength;
        if (effectiveLength is null || effectiveLength <= 0)
        {
            return DataType.NVarCharMax;
        }

        if (effectiveLength == -1)
        {
            return DataType.NVarCharMax;
        }

        if (maxThreshold is > 0 && effectiveLength > maxThreshold)
        {
            return DataType.NVarCharMax;
        }

        return DataType.NVarChar(effectiveLength.Value);
    }

    private static DataType ResolveVarCharText(int? length, int? fallbackLength)
    {
        var effectiveLength = length ?? fallbackLength;
        if (effectiveLength is null || effectiveLength <= 0)
        {
            if (fallbackLength is > 0)
            {
                return DataType.VarChar(fallbackLength.Value);
            }

            return DataType.VarCharMax;
        }

        return effectiveLength == -1 ? DataType.VarCharMax : DataType.VarChar(effectiveLength.Value);
    }

    private static DataType ResolveVarBinary(int? length)
    {
        if (length is null or <= 0)
        {
            return DataType.VarBinaryMax;
        }

        return length == -1 ? DataType.VarBinaryMax : DataType.VarBinary(length.Value);
    }

    private static DataType ResolveDecimal(int? precision, int? scale, int? defaultPrecision, int? defaultScale)
    {
        var resolvedPrecision = ResolvePrecision(null, precision, defaultPrecision ?? 18);
        var resolvedScale = ResolveScale(null, scale, defaultScale ?? 0);
        return DataType.Decimal(resolvedPrecision, resolvedScale);
    }

    private static DataType ResolveDateTime(string? sqlType, int? attributeScale, int? defaultScale)
    {
        var scale = ResolveScale(null, attributeScale, defaultScale ?? 0);
        return NormalizeDateTimeType(sqlType, scale);
    }

    private static DataType NormalizeDateTimeType(string? sqlType, int scale)
    {
        var normalized = (sqlType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "datetime2" => DataType.DateTime2(Math.Clamp(scale, 0, 7)),
            "datetimeoffset" => DataType.DateTimeOffset(Math.Clamp(scale, 0, 7)),
            "time" => DataType.Time(Math.Clamp(scale, 0, 7)),
            "smalldatetime" => DataType.SmallDateTime,
            "date" => DataType.Date,
            "datetime" => DataType.DateTime,
            _ => DataType.DateTime,
        };
    }

    private static bool TryResolveFromOnDisk(AttributeModel attribute, AttributeOnDiskMetadata onDisk, out DataType dataType)
    {
        dataType = DataType.NVarCharMax;
        if (onDisk is null || string.IsNullOrWhiteSpace(onDisk.SqlType))
        {
            return false;
        }

        var type = onDisk.SqlType!.Trim().ToLowerInvariant();
        switch (type)
        {
            case "nvarchar":
                dataType = ResolveUnicodeText(onDisk.MaxLength ?? attribute.Length, 2000, null);
                return true;
            case "nchar":
                dataType = DataType.NChar(Math.Max(1, (onDisk.MaxLength ?? attribute.Length) ?? 1));
                return true;
            case "varchar":
                dataType = ResolveVarCharText(onDisk.MaxLength ?? attribute.Length, null);
                return true;
            case "char":
                dataType = DataType.Char(Math.Max(1, (onDisk.MaxLength ?? attribute.Length) ?? 1));
                return true;
            case "varbinary":
                dataType = ResolveVarBinary(onDisk.MaxLength ?? attribute.Length);
                return true;
            case "binary":
                dataType = DataType.Binary(Math.Max(1, (onDisk.MaxLength ?? attribute.Length) ?? 1));
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
                return ResolveVarCharText(varcharLength, null);
            case "nvarchar":
                var nvarcharLength = parameters.Count > 0 ? parameters[0] : 0;
                return ResolveUnicodeText(nvarcharLength, 2000, null);
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

    private static DataType CreateFixedType(string? sqlType)
    {
        var normalized = (sqlType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "bigint" => DataType.BigInt,
            "int" => DataType.Int,
            "smallint" => DataType.SmallInt,
            "tinyint" => DataType.TinyInt,
            "bit" => DataType.Bit,
            "datetime" => DataType.DateTime,
            "smalldatetime" => DataType.SmallDateTime,
            "datetime2" => DataType.DateTime2(7),
            "datetimeoffset" => DataType.DateTimeOffset(7),
            "time" => DataType.Time(7),
            "date" => DataType.Date,
            "float" => DataType.Float,
            "real" => DataType.Real,
            "money" => DataType.Money,
            "smallmoney" => DataType.SmallMoney,
            "uniqueidentifier" => DataType.UniqueIdentifier,
            "image" => DataType.Image,
            "text" => DataType.Text,
            "ntext" => DataType.NText,
            _ => DataType.NVarCharMax,
        };
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record TypeMappingRule
    {
        public TypeMappingKind Kind { get; init; }
        public DataType? FixedType { get; init; }
        public int? MaxLengthThreshold { get; init; }
        public int? FallbackLength { get; init; }
        public int? DefaultPrecision { get; init; }
        public int? DefaultScale { get; init; }
        public string? SqlTypeName { get; init; }
        public TypeMappingIdentifierRule? Identifier { get; init; }

        public static TypeMappingRule Create(TypeMappingRuleDefinition definition, bool allowIdentifier = true)
        {
            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var kind = ParseKind(definition.Kind);
            if (!allowIdentifier && kind == TypeMappingKind.Identifier)
            {
                throw new InvalidOperationException("Identifier mapping cannot be declared as the default rule.");
            }

            return kind switch
            {
                TypeMappingKind.Fixed => new TypeMappingRule
                {
                    Kind = kind,
                    FixedType = CreateFixedType(definition.SqlType),
                    SqlTypeName = definition.SqlType,
                },
                TypeMappingKind.Unicode => new TypeMappingRule
                {
                    Kind = kind,
                    MaxLengthThreshold = definition.MaxLength,
                    FallbackLength = definition.FallbackLength ?? definition.MaxLength,
                },
                TypeMappingKind.VarChar => new TypeMappingRule
                {
                    Kind = kind,
                    FallbackLength = definition.FallbackLength,
                    SqlTypeName = string.IsNullOrWhiteSpace(definition.SqlType) ? "varchar" : definition.SqlType,
                },
                TypeMappingKind.VarBinary => new TypeMappingRule { Kind = kind },
                TypeMappingKind.Decimal => new TypeMappingRule
                {
                    Kind = kind,
                    DefaultPrecision = definition.Precision,
                    DefaultScale = definition.Scale,
                },
                TypeMappingKind.DateTime => new TypeMappingRule
                {
                    Kind = kind,
                    SqlTypeName = string.IsNullOrWhiteSpace(definition.SqlType) ? "datetime" : definition.SqlType,
                    DefaultScale = definition.Scale,
                },
                TypeMappingKind.Identifier => new TypeMappingRule
                {
                    Kind = kind,
                    Identifier = TypeMappingIdentifierRule.Create(definition),
                },
                _ => CreateDefault(),
            };
        }

        public static TypeMappingRule CreateDefault()
        {
            return new TypeMappingRule
            {
                Kind = TypeMappingKind.Unicode,
                MaxLengthThreshold = null,
                FallbackLength = null,
            };
        }

        private static TypeMappingKind ParseKind(string? kind)
        {
            var normalized = (kind ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "fixed" => TypeMappingKind.Fixed,
                "unicode" => TypeMappingKind.Unicode,
                "varchar" => TypeMappingKind.VarChar,
                "varbinary" => TypeMappingKind.VarBinary,
                "decimal" => TypeMappingKind.Decimal,
                "datetime" => TypeMappingKind.DateTime,
                "identifier" => TypeMappingKind.Identifier,
                _ => TypeMappingKind.Unicode,
            };
        }
    }

    private sealed record TypeMappingIdentifierRule(DataType PrimaryKeyType, DataType? ForeignKeyType, DataType? DefaultType)
    {
        public static TypeMappingIdentifierRule Create(TypeMappingRuleDefinition definition)
        {
            var primary = CreateFixedType(definition.PrimaryKeyType);
            var foreign = string.IsNullOrWhiteSpace(definition.ForeignKeyType)
                ? (DataType?)null
                : CreateFixedType(definition.ForeignKeyType);
            var defaultType = string.IsNullOrWhiteSpace(definition.DefaultType)
                ? (DataType?)null
                : CreateFixedType(definition.DefaultType);
            return new TypeMappingIdentifierRule(primary, foreign, defaultType);
        }

        public static TypeMappingIdentifierRule BigInt { get; } = new(DataType.BigInt, DataType.BigInt, DataType.BigInt);
    }

    private sealed class TypeMappingPolicyDocument
    {
        public TypeMappingRuleDefinition? Default { get; init; }

        public IList<TypeMappingRuleDefinition>? Rules { get; init; }
    }

    private sealed class TypeMappingRuleDefinition
    {
        public string? LogicalType { get; init; }
        public string? Kind { get; init; }
        public string? SqlType { get; init; }
        public int? MaxLength { get; init; }
        public int? FallbackLength { get; init; }
        public int? Precision { get; init; }
        public int? Scale { get; init; }
        public string? PrimaryKeyType { get; init; }
        public string? ForeignKeyType { get; init; }
        public string? DefaultType { get; init; }
    }

    private enum TypeMappingKind
    {
        Fixed,
        Unicode,
        VarChar,
        VarBinary,
        Decimal,
        DateTime,
        Identifier,
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;

namespace Osm.Smo;

public sealed class TypeMappingPolicy
{
    private const int DefaultUnicodeMaxLengthThreshold = 2000;
    private const int DefaultVarBinaryMaxLengthThreshold = 2000;

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

    public DataType Resolve(AttributeModel attribute)
    {
        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        var normalized = TypeMappingKeyNormalizer.Normalize(attribute.DataType);
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

        var key = TypeMappingKeyNormalizer.Normalize(onDisk.SqlType);
        if (!_onDiskRules.TryGetValue(key, out var rule))
        {
            return false;
        }

        dataType = rule.Apply(TypeMappingRequest.ForOnDisk(attribute, onDisk));
        return true;
    }

    private DataType ResolveExternal(AttributeModel attribute, string externalType)
    {
        var (baseType, parameters) = TypeMappingExternalTypeParser.Parse(externalType);
        var key = TypeMappingKeyNormalizer.Normalize(baseType);

        if (_externalRules.TryGetValue(key, out var rule))
        {
            return rule.Apply(TypeMappingRequest.ForExternal(attribute, parameters));
        }

        return _defaultRule.Apply(TypeMappingRequest.ForExternal(attribute, parameters));
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

    private static DataType ResolveFixedSqlType(string? sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType))
        {
            return DataType.NVarCharMax;
        }

        var (baseType, parameters) = TypeMappingExternalTypeParser.Parse(sqlType);
        var key = TypeMappingKeyNormalizer.Normalize(baseType);

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

    internal enum TypeResolutionSource
    {
        Attribute,
        OnDisk,
        External,
    }

    internal sealed class TypeMappingRequest
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

    internal sealed class TypeMappingRule
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

        internal TypeMappingRule(TypeMappingRuleDefinition definition, TypeResolutionSource source)
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

        internal DataType Apply(TypeMappingRequest request)
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

}

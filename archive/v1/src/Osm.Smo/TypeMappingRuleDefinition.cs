using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Osm.Smo;

public enum TypeMappingStrategy
{
    Fixed,
    UnicodeText,
    VarChar,
    VarCharText,
    VarBinary,
    Decimal,
    DateTime2,
    DateTimeOffset,
    Time,
    Char,
    NChar,
    Binary,
}

public enum TypeValueSource
{
    Attribute,
    OnDisk,
    OnDiskOrAttribute,
    Parameters,
}

public sealed record TypeMappingRuleDefinition(
    TypeMappingStrategy Strategy,
    string? SqlType,
    int? FallbackLength,
    int? DefaultPrecision,
    int? DefaultScale,
    int? Scale,
    int? MaxLengthThreshold,
    TypeValueSource? LengthSource = null,
    TypeValueSource? PrecisionSource = null,
    TypeValueSource? ScaleSource = null,
    int? LengthParameterIndex = null,
    int? PrecisionParameterIndex = null,
    int? ScaleParameterIndex = null)
{
    private const string StrategyProperty = "strategy";
    private const string SqlTypeProperty = "sqlType";
    private const string FallbackLengthProperty = "fallbackLength";
    private const string DefaultPrecisionProperty = "defaultPrecision";
    private const string DefaultScaleProperty = "defaultScale";
    private const string ScaleProperty = "scale";
    private const string MaxLengthThresholdProperty = "maxLengthThreshold";
    private const string LengthSourceProperty = "lengthSource";
    private const string PrecisionSourceProperty = "precisionSource";
    private const string ScaleSourceProperty = "scaleSource";
    private const string LengthParameterIndexProperty = "lengthParameterIndex";
    private const string PrecisionParameterIndexProperty = "precisionParameterIndex";
    private const string ScaleParameterIndexProperty = "scaleParameterIndex";

    public static bool TryParse(JsonElement element, out TypeMappingRuleDefinition definition, out string? error)
    {
        definition = null!;
        error = null;

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var sqlType = element.GetString();
                if (string.IsNullOrWhiteSpace(sqlType))
                {
                    error = "Type mapping string value must specify a SQL type.";
                    return false;
                }

                definition = new TypeMappingRuleDefinition(
                    TypeMappingStrategy.Fixed,
                    sqlType.Trim(),
                    FallbackLength: null,
                    DefaultPrecision: null,
                    DefaultScale: null,
                    Scale: null,
                    MaxLengthThreshold: null);
                return true;
            }

            case JsonValueKind.Object:
            {
                if (!TryReadStrategy(element, out var strategy, out error))
                {
                    return false;
                }

                if (!TryReadString(element, SqlTypeProperty, out var sqlType))
                {
                    return false;
                }

                if (strategy == TypeMappingStrategy.Fixed && string.IsNullOrWhiteSpace(sqlType))
                {
                    error = "Fixed strategy requires 'sqlType'.";
                    return false;
                }

                if (!TryReadInt(element, FallbackLengthProperty, out var fallbackLength, out error) ||
                    !TryReadInt(element, DefaultPrecisionProperty, out var defaultPrecision, out error) ||
                    !TryReadInt(element, DefaultScaleProperty, out var defaultScale, out error) ||
                    !TryReadInt(element, ScaleProperty, out var scale, out error) ||
                    !TryReadInt(element, MaxLengthThresholdProperty, out var maxLengthThreshold, out error) ||
                    !TryReadValueSource(element, LengthSourceProperty, out var lengthSource, out error) ||
                    !TryReadValueSource(element, PrecisionSourceProperty, out var precisionSource, out error) ||
                    !TryReadValueSource(element, ScaleSourceProperty, out var scaleSource, out error) ||
                    !TryReadInt(element, LengthParameterIndexProperty, out var lengthParameterIndex, out error) ||
                    !TryReadInt(element, PrecisionParameterIndexProperty, out var precisionParameterIndex, out error) ||
                    !TryReadInt(element, ScaleParameterIndexProperty, out var scaleParameterIndex, out error))
                {
                    return false;
                }

                definition = new TypeMappingRuleDefinition(
                    strategy,
                    string.IsNullOrWhiteSpace(sqlType) ? null : sqlType!.Trim(),
                    fallbackLength,
                    defaultPrecision,
                    defaultScale,
                    scale,
                    maxLengthThreshold,
                    lengthSource,
                    precisionSource,
                    scaleSource,
                    lengthParameterIndex,
                    precisionParameterIndex,
                    scaleParameterIndex);
                return true;
            }

            default:
                error = "Type mapping rule must be defined as a string or object.";
                return false;
        }
    }

    private static bool TryReadStrategy(JsonElement element, out TypeMappingStrategy strategy, out string? error)
    {
        strategy = TypeMappingStrategy.Fixed;
        error = null;

        if (!element.TryGetProperty(StrategyProperty, out var strategyElement))
        {
            if (element.TryGetProperty(SqlTypeProperty, out var sqlElement) &&
                sqlElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(sqlElement.GetString()))
            {
                strategy = TypeMappingStrategy.Fixed;
                return true;
            }

            error = "Type mapping rule must specify either 'strategy' or 'sqlType'.";
            return false;
        }

        var strategyText = strategyElement.GetString();
        if (string.IsNullOrWhiteSpace(strategyText))
        {
            error = "Type mapping strategy cannot be empty.";
            return false;
        }

        if (!Enum.TryParse(strategyText, ignoreCase: true, out strategy))
        {
            error = $"Unrecognized type mapping strategy '{strategyText}'.";
            return false;
        }

        return true;
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryReadInt(JsonElement element, string propertyName, out int? value, out string? error)
    {
        value = null;
        error = null;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numeric))
        {
            value = numeric;
            return true;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var text = property.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (int.TryParse(text, out numeric))
            {
                value = numeric;
                return true;
            }
        }

        error = $"Property '{propertyName}' must be an integer.";
        return false;
    }

    private static bool TryReadValueSource(JsonElement element, string propertyName, out TypeValueSource? value, out string? error)
    {
        value = null;
        error = null;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = $"Property '{propertyName}' must be a string.";
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!Enum.TryParse(text, ignoreCase: true, out TypeValueSource parsed))
        {
            error = $"Unrecognized value source '{text}' for property '{propertyName}'.";
            return false;
        }

        value = parsed;
        return true;
    }

    public string ToMetadataString()
    {
        var parts = new List<string>
        {
            $"strategy={Strategy}"
        };

        if (!string.IsNullOrWhiteSpace(SqlType))
        {
            parts.Add($"sqlType={SqlType}");
        }

        if (FallbackLength.HasValue)
        {
            parts.Add($"fallbackLength={FallbackLength.Value}");
        }

        if (DefaultPrecision.HasValue)
        {
            parts.Add($"defaultPrecision={DefaultPrecision.Value}");
        }

        if (DefaultScale.HasValue)
        {
            parts.Add($"defaultScale={DefaultScale.Value}");
        }

        if (Scale.HasValue)
        {
            parts.Add($"scale={Scale.Value}");
        }

        if (MaxLengthThreshold.HasValue)
        {
            parts.Add($"maxLengthThreshold={MaxLengthThreshold.Value}");
        }

        if (LengthSource.HasValue)
        {
            parts.Add($"lengthSource={LengthSource.Value}");
        }

        if (PrecisionSource.HasValue)
        {
            parts.Add($"precisionSource={PrecisionSource.Value}");
        }

        if (ScaleSource.HasValue)
        {
            parts.Add($"scaleSource={ScaleSource.Value}");
        }

        if (LengthParameterIndex.HasValue)
        {
            parts.Add($"lengthParameterIndex={LengthParameterIndex.Value}");
        }

        if (PrecisionParameterIndex.HasValue)
        {
            parts.Add($"precisionParameterIndex={PrecisionParameterIndex.Value}");
        }

        if (ScaleParameterIndex.HasValue)
        {
            parts.Add($"scaleParameterIndex={ScaleParameterIndex.Value}");
        }

        return string.Join(';', parts);
    }
}

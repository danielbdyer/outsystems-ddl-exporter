using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Model;

namespace Osm.Smo;

internal static class SqlDataTypeMapper
{
    public static DataType Resolve(AttributeModel attribute)
    {
        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        if (TryResolveFromOnDisk(attribute, attribute.OnDisk, out var onDiskType))
        {
            return onDiskType;
        }

        if (!string.IsNullOrWhiteSpace(attribute.ExternalDatabaseType))
        {
            return ResolveExternal(attribute.ExternalDatabaseType!);
        }

        var normalized = NormalizeDataType(attribute.DataType);
        return normalized switch
        {
            "identifier" => DataType.Int,
            "autonumber" => DataType.BigInt,
            "integer" => DataType.Int,
            "longinteger" => DataType.BigInt,
            "boolean" => DataType.Bit,
            "datetime" => DataType.DateTime,
            "datetime2" => DataType.DateTime2(ResolveScale(null, attribute.Scale, 7)),
            "datetimeoffset" => DataType.DateTimeOffset(ResolveScale(null, attribute.Scale, 7)),
            "date" => DataType.Date,
            "time" => DataType.Time(ResolveScale(null, attribute.Scale, 7)),
            "decimal" => ResolveDecimal(attribute.Precision, attribute.Scale),
            "double" => DataType.Float,
            "float" => DataType.Float,
            "real" => DataType.Real,
            "currency" => ResolveDecimal(attribute.Precision, attribute.Scale, 19, 4),
            "binarydata" => ResolveVarBinary(attribute.Length),
            "binary" => ResolveVarBinary(attribute.Length),
            "varbinary" => ResolveVarBinary(attribute.Length),
            "longbinarydata" => ResolveVarBinary(attribute.Length),
            "image" => DataType.Image,
            "longtext" => DataType.NVarCharMax,
            "text" => ResolveUnicodeText(attribute.Length),
            "email" => ResolveUnicodeText(attribute.Length, 254),
            "phonenumber" => ResolveUnicodeText(attribute.Length, 50),
            "phone" => ResolveUnicodeText(attribute.Length, 50),
            "url" => ResolveUnicodeText(attribute.Length),
            "password" => ResolveUnicodeText(attribute.Length),
            "username" => ResolveUnicodeText(attribute.Length),
            "identifiertext" => ResolveUnicodeText(attribute.Length),
            "guid" => DataType.UniqueIdentifier,
            "uniqueidentifier" => DataType.UniqueIdentifier,
            _ => DataType.NVarCharMax,
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
                dataType = ResolveUnicodeText(onDisk.MaxLength ?? attribute.Length);
                return true;
            case "nchar":
                dataType = DataType.NChar(Math.Max(1, (onDisk.MaxLength ?? attribute.Length) ?? 1));
                return true;
            case "varchar":
                dataType = ResolveVarChar(onDisk.MaxLength ?? attribute.Length);
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

    private static DataType ResolveUnicodeText(int? length, int? fallbackLength = null)
    {
        var effectiveLength = length ?? fallbackLength;
        if (effectiveLength is null or <= 0)
        {
            return DataType.NVarCharMax;
        }

        return effectiveLength == -1 ? DataType.NVarCharMax : DataType.NVarChar(effectiveLength.Value);
    }

    private static DataType ResolveVarChar(int? length)
    {
        if (length is null or <= 0)
        {
            return DataType.VarCharMax;
        }

        return length == -1 ? DataType.VarCharMax : DataType.VarChar(length.Value);
    }

    private static DataType ResolveVarBinary(int? length)
    {
        if (length is null or <= 0)
        {
            return DataType.VarBinaryMax;
        }

        return length == -1 ? DataType.VarBinaryMax : DataType.VarBinary(length.Value);
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
                return ResolveUnicodeText(nvarcharLength);
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
}

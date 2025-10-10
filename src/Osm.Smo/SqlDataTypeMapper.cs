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

        if (TryResolveFromOnDisk(attribute.OnDisk, out var onDiskType))
        {
            return onDiskType;
        }

        if (!string.IsNullOrWhiteSpace(attribute.ExternalDatabaseType))
        {
            return ResolveExternal(attribute.ExternalDatabaseType!);
        }

        var normalized = attribute.DataType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "identifier" => DataType.Int,
            "text" => ResolveText(attribute.Length),
            "longtext" => DataType.NVarCharMax,
            "boolean" => DataType.Bit,
            "datetime" => DataType.DateTime,
            "date" => DataType.Date,
            "decimal" or "double" => ResolveDecimal(attribute.Precision, attribute.Scale),
            _ => DataType.NVarCharMax,
        };
    }

    private static bool TryResolveFromOnDisk(AttributeOnDiskMetadata onDisk, out DataType dataType)
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
                dataType = ResolveUnicodeText(onDisk.MaxLength);
                return true;
            case "nchar":
                dataType = DataType.NChar(Math.Max(1, onDisk.MaxLength ?? 1));
                return true;
            case "varchar":
                dataType = ResolveVarChar(onDisk.MaxLength);
                return true;
            case "char":
                dataType = DataType.Char(Math.Max(1, onDisk.MaxLength ?? 1));
                return true;
            case "varbinary":
                dataType = ResolveVarBinary(onDisk.MaxLength);
                return true;
            case "binary":
                dataType = DataType.Binary(Math.Max(1, onDisk.MaxLength ?? 1));
                return true;
            case "decimal":
            case "numeric":
                dataType = DataType.Decimal(
                    onDisk.Precision is null or <= 0 ? 18 : onDisk.Precision.Value,
                    onDisk.Scale is null or < 0 ? 0 : onDisk.Scale.Value);
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
                dataType = DataType.DateTime2(onDisk.Scale is null or < 0 ? 7 : onDisk.Scale.Value);
                return true;
            case "datetimeoffset":
                dataType = DataType.DateTimeOffset(onDisk.Scale is null or < 0 ? 7 : onDisk.Scale.Value);
                return true;
            case "date":
                dataType = DataType.Date;
                return true;
            case "time":
                dataType = DataType.Time(onDisk.Scale is null or < 0 ? 7 : onDisk.Scale.Value);
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

    private static DataType ResolveText(int? length)
    {
        if (length is null or <= 0)
        {
            return DataType.NVarCharMax;
        }

        return DataType.NVarChar(length.Value);
    }

    private static DataType ResolveUnicodeText(int? length)
    {
        if (length is null or <= 0)
        {
            return DataType.NVarCharMax;
        }

        return length == -1 ? DataType.NVarCharMax : DataType.NVarChar(length.Value);
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

    private static DataType ResolveDecimal(int? precision, int? scale)
    {
        var resolvedPrecision = precision is null or <= 0 ? 18 : precision.Value;
        var resolvedScale = scale is null or < 0 ? 0 : scale.Value;
        return DataType.Decimal(resolvedPrecision, resolvedScale);
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
            .Select(p => int.TryParse(p.Trim(), out var value) ? value : 0)
            .ToArray();

        return ResolveExternalCore(baseType, parts);
    }

    private static DataType ResolveExternalCore(string baseType, IReadOnlyList<int> parameters)
    {
        switch (baseType.ToLowerInvariant())
        {
            case "varchar":
            case "nvarchar":
                var length = parameters.Count > 0 ? parameters[0] : 0;
                return length <= 0 ? DataType.VarCharMax : DataType.VarChar(length);
            case "nchar":
            case "char":
                var charLength = parameters.Count > 0 ? parameters[0] : 0;
                return charLength <= 0 ? DataType.Char(1) : DataType.Char(charLength);
            case "decimal":
            case "numeric":
                var precision = parameters.Count > 0 ? parameters[0] : 18;
                var scale = parameters.Count > 1 ? parameters[1] : 0;
                return DataType.Decimal(precision, scale);
            case "int":
                return DataType.Int;
            case "bigint":
                return DataType.BigInt;
            case "bit":
                return DataType.Bit;
            case "datetime":
                return DataType.DateTime;
            default:
                return DataType.NVarCharMax;
        }
    }
}

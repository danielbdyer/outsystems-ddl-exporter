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

    private static DataType ResolveText(int? length)
    {
        if (length is null or <= 0)
        {
            return DataType.NVarCharMax;
        }

        return DataType.NVarChar(length.Value);
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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Osm.Domain.Model;

namespace Osm.Smo;

internal static class ForeignKeyNameFactory
{
    private const int MaxConstraintNameLength = 128;
    private const int HashLength = 12;
    private const string ForeignKeyPrefix = "FK";

    public static string CreateEvidenceName(
        EntityEmissionContext ownerContext,
        EntityEmissionContext referencedContext,
        string? providedConstraintName,
        ImmutableArray<string> ownerColumns,
        ImmutableArray<AttributeModel> ownerAttributes,
        string referencedTable,
        SmoFormatOptions format)
    {
        if (ownerContext is null)
        {
            throw new ArgumentNullException(nameof(ownerContext));
        }

        if (referencedContext is null)
        {
            throw new ArgumentNullException(nameof(referencedContext));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        var baseName = string.IsNullOrWhiteSpace(providedConstraintName)
            ? BuildPhysicalBaseName(ownerContext.Entity.PhysicalName.Value, referencedTable, ownerColumns)
            : AppendColumnSegment(providedConstraintName!, ownerColumns);

        baseName = ForceForeignKeyPrefix(baseName);
        baseName = EnforceLengthConstraints(
            baseName,
            ownerContext,
            referencedContext,
            ownerColumns,
            ownerAttributes);

        return ConstraintNameNormalizer.Normalize(
            baseName,
            ownerContext.Entity,
            ownerAttributes,
            ConstraintNameKind.ForeignKey,
            format,
            referencedEntity: referencedContext.Entity);
    }

    public static string CreateFallbackName(
        EntityEmissionContext ownerContext,
        EntityEmissionContext referencedContext,
        AttributeModel attribute,
        SmoFormatOptions format)
    {
        if (ownerContext is null)
        {
            throw new ArgumentNullException(nameof(ownerContext));
        }

        if (referencedContext is null)
        {
            throw new ArgumentNullException(nameof(referencedContext));
        }

        if (attribute is null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        var ownerColumns = ImmutableArray.Create(attribute.ColumnName.Value);
        var ownerAttributes = ImmutableArray.Create(attribute);
        var baseName = BuildPhysicalBaseName(
            ownerContext.Entity.PhysicalName.Value,
            referencedContext.Entity.PhysicalName.Value,
            ownerColumns);

        baseName = EnforceLengthConstraints(
            baseName,
            ownerContext,
            referencedContext,
            ownerColumns,
            ownerAttributes);

        return ConstraintNameNormalizer.Normalize(
            baseName,
            ownerContext.Entity,
            ownerAttributes,
            ConstraintNameKind.ForeignKey,
            format,
            referencedEntity: referencedContext.Entity);
    }

    private static string AppendColumnSegment(string name, ImmutableArray<string> ownerColumns)
    {
        if (string.IsNullOrWhiteSpace(name) || ownerColumns.IsDefaultOrEmpty)
        {
            return name;
        }

        var needsAppend = false;
        foreach (var column in ownerColumns)
        {
            if (!name.Contains(column, StringComparison.OrdinalIgnoreCase))
            {
                needsAppend = true;
                break;
            }
        }

        if (!needsAppend)
        {
            return name;
        }

        var segment = string.Join('_', ownerColumns);
        return $"{name}_{segment}";
    }

    private static string BuildPhysicalBaseName(
        string ownerPhysicalName,
        string referencedPhysicalName,
        ImmutableArray<string> ownerColumns)
    {
        var columnSegment = ownerColumns.IsDefaultOrEmpty
            ? string.Empty
            : string.Join('_', ownerColumns);

        return string.IsNullOrEmpty(columnSegment)
            ? $"{ForeignKeyPrefix}_{ownerPhysicalName}_{referencedPhysicalName}"
            : $"{ForeignKeyPrefix}_{ownerPhysicalName}_{referencedPhysicalName}_{columnSegment}";
    }

    private static string ForceForeignKeyPrefix(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var trimmed = name.Trim();
        var separatorIndex = trimmed.IndexOf('_');
        if (separatorIndex < 0)
        {
            return $"{ForeignKeyPrefix}_{trimmed}";
        }

        var suffix = trimmed[(separatorIndex + 1)..];
        return string.IsNullOrEmpty(suffix)
            ? ForeignKeyPrefix
            : $"{ForeignKeyPrefix}_{suffix}";
    }

    private static string EnforceLengthConstraints(
        string baseName,
        EntityEmissionContext ownerContext,
        EntityEmissionContext referencedContext,
        ImmutableArray<string> ownerColumns,
        ImmutableArray<AttributeModel> ownerAttributes)
    {
        if (string.IsNullOrWhiteSpace(baseName) || baseName.Length <= MaxConstraintNameLength)
        {
            return baseName;
        }

        var logicalBase = BuildLogicalBaseName(
            ownerContext,
            referencedContext,
            ownerColumns,
            ownerAttributes);

        if (logicalBase.Length <= MaxConstraintNameLength)
        {
            return logicalBase;
        }

        return TruncateWithHash(logicalBase);
    }

    private static string BuildLogicalBaseName(
        EntityEmissionContext ownerContext,
        EntityEmissionContext referencedContext,
        ImmutableArray<string> ownerColumns,
        ImmutableArray<AttributeModel> ownerAttributes)
    {
        var columnNames = ResolveOwnerColumnNames(ownerColumns, ownerAttributes);
        var builder = new StringBuilder();
        builder.Append(ForeignKeyPrefix);
        builder.Append('_');
        builder.Append(ownerContext.Entity.LogicalName.Value);
        builder.Append('_');
        builder.Append(referencedContext.Entity.LogicalName.Value);

        if (columnNames.Count > 0)
        {
            builder.Append('_');
            builder.Append(string.Join('_', columnNames));
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ResolveOwnerColumnNames(
        ImmutableArray<string> ownerColumns,
        ImmutableArray<AttributeModel> ownerAttributes)
    {
        if (!ownerAttributes.IsDefaultOrEmpty)
        {
            var logicalColumns = ownerAttributes
                .Where(static attribute => attribute is not null)
                .Select(static attribute => attribute.ColumnName.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            if (logicalColumns.Length > 0)
            {
                return logicalColumns;
            }
        }

        if (!ownerColumns.IsDefaultOrEmpty)
        {
            var normalized = ownerColumns
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            if (normalized.Length > 0)
            {
                return normalized;
            }
        }

        return Array.Empty<string>();
    }

    private static string TruncateWithHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= MaxConstraintNameLength)
        {
            return value;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var usableHashLength = Math.Min(HashLength, hash.Length);
        var available = MaxConstraintNameLength - usableHashLength - 1;

        if (available <= 0)
        {
            return hash[..Math.Min(MaxConstraintNameLength, hash.Length)];
        }

        var prefix = value[..Math.Min(value.Length, available)].TrimEnd('_');
        if (prefix.Length == 0)
        {
            prefix = value[..Math.Min(value.Length, available)];
        }

        var suffix = hash[..usableHashLength];
        return string.IsNullOrEmpty(prefix)
            ? suffix[..Math.Min(MaxConstraintNameLength, suffix.Length)]
            : $"{prefix}_{suffix}";
    }
}

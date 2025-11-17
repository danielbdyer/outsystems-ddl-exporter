using System;
using System.Collections.Generic;

namespace Osm.Pipeline.ModelIngestion;

internal readonly record struct RelationshipConstraintKey
{
    public RelationshipConstraintKey(string schema, string table, string constraintName)
    {
        Schema = schema?.Trim() ?? string.Empty;
        Table = table?.Trim() ?? string.Empty;
        ConstraintName = constraintName?.Trim() ?? string.Empty;
    }

    public string Schema { get; }

    public string Table { get; }

    public string ConstraintName { get; }

    public bool IsValid
        => !string.IsNullOrEmpty(Schema)
            && !string.IsNullOrEmpty(Table)
            && !string.IsNullOrEmpty(ConstraintName);
}

internal sealed class RelationshipConstraintKeyComparer : IEqualityComparer<RelationshipConstraintKey>
{
    public static RelationshipConstraintKeyComparer Instance { get; } = new();

    public bool Equals(RelationshipConstraintKey x, RelationshipConstraintKey y)
    {
        return string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ConstraintName, y.ConstraintName, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(RelationshipConstraintKey obj)
    {
        return HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ConstraintName));
    }
}

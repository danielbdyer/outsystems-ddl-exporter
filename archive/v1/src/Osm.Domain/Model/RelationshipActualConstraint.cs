using System.Collections.Immutable;

namespace Osm.Domain.Model;

public sealed record RelationshipActualConstraint(
    string Name,
    string ReferencedSchema,
    string ReferencedTable,
    string OnDeleteAction,
    string OnUpdateAction,
    ImmutableArray<RelationshipActualConstraintColumn> Columns)
{
    public static RelationshipActualConstraint Create(
        string name,
        string? referencedSchema,
        string? referencedTable,
        string? onDeleteAction,
        string? onUpdateAction,
        IEnumerable<RelationshipActualConstraintColumn> columns)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        var materialized = columns.ToImmutableArray();
        return new RelationshipActualConstraint(
            string.IsNullOrWhiteSpace(name) ? "" : name.Trim(),
            string.IsNullOrWhiteSpace(referencedSchema) ? string.Empty : referencedSchema!.Trim(),
            string.IsNullOrWhiteSpace(referencedTable) ? string.Empty : referencedTable!.Trim(),
            string.IsNullOrWhiteSpace(onDeleteAction) ? string.Empty : onDeleteAction!.Trim(),
            string.IsNullOrWhiteSpace(onUpdateAction) ? string.Empty : onUpdateAction!.Trim(),
            materialized);
    }
}

public sealed record RelationshipActualConstraintColumn(
    string OwnerColumn,
    string OwnerAttribute,
    string ReferencedColumn,
    string ReferencedAttribute,
    int Ordinal)
{
    public static RelationshipActualConstraintColumn Create(
        string? ownerColumn,
        string? ownerAttribute,
        string? referencedColumn,
        string? referencedAttribute,
        int ordinal)
    {
        return new RelationshipActualConstraintColumn(
            string.IsNullOrWhiteSpace(ownerColumn) ? string.Empty : ownerColumn!.Trim(),
            string.IsNullOrWhiteSpace(ownerAttribute) ? string.Empty : ownerAttribute!.Trim(),
            string.IsNullOrWhiteSpace(referencedColumn) ? string.Empty : referencedColumn!.Trim(),
            string.IsNullOrWhiteSpace(referencedAttribute) ? string.Empty : referencedAttribute!.Trim(),
            ordinal);
    }
}

using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public sealed record ForeignKeyModel(
    ForeignKeyName Name,
    ModuleName TargetModule,
    EntityName TargetEntity,
    ImmutableArray<ColumnName> FromColumns,
    ImmutableArray<ColumnName> ToColumns,
    string DeleteRule,
    string UpdateRule)
{
    public static Result<ForeignKeyModel> Create(
        ForeignKeyName name,
        ModuleName targetModule,
        EntityName targetEntity,
        IEnumerable<ColumnName> fromColumns,
        IEnumerable<ColumnName> toColumns,
        string deleteRule,
        string updateRule)
    {
        if (fromColumns is null)
        {
            throw new ArgumentNullException(nameof(fromColumns));
        }

        if (toColumns is null)
        {
            throw new ArgumentNullException(nameof(toColumns));
        }

        if (string.IsNullOrWhiteSpace(deleteRule))
        {
            return Result<ForeignKeyModel>.Failure(ValidationError.Create("fk.deleteRule.invalid", "Delete rule must be provided."));
        }

        if (string.IsNullOrWhiteSpace(updateRule))
        {
            return Result<ForeignKeyModel>.Failure(ValidationError.Create("fk.updateRule.invalid", "Update rule must be provided."));
        }

        var from = fromColumns.ToImmutableArray();
        var to = toColumns.ToImmutableArray();

        if (from.IsDefaultOrEmpty)
        {
            return Result<ForeignKeyModel>.Failure(ValidationError.Create("fk.columns.empty", "Foreign key must include at least one source column."));
        }

        if (from.Length != to.Length)
        {
            return Result<ForeignKeyModel>.Failure(ValidationError.Create("fk.columns.mismatch", "Foreign key source and target column counts must match."));
        }

        return Result<ForeignKeyModel>.Success(new ForeignKeyModel(
            name,
            targetModule,
            targetEntity,
            from,
            to,
            deleteRule.Trim(),
            updateRule.Trim()));
    }
}

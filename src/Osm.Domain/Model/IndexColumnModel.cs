using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public sealed record IndexColumnModel(
    AttributeName Attribute,
    ColumnName Column,
    int Ordinal)
{
    public static Result<IndexColumnModel> Create(
        AttributeName attribute,
        ColumnName column,
        int ordinal)
    {
        if (ordinal <= 0)
        {
            return Result<IndexColumnModel>.Failure(ValidationError.Create("index.column.ordinal.invalid", "Index column ordinal must be positive."));
        }

        return Result<IndexColumnModel>.Success(new IndexColumnModel(attribute, column, ordinal));
    }
}

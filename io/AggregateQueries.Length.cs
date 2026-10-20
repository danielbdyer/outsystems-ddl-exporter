using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnType = DbChange.Kernel.ColumnType;

namespace DbChange.Io;

/// <summary>
/// KeyLength (WP 2.1's second slice): the rows whose key passes the index's limit, 900 bytes for a clustered index and 1,700 for another,
/// on which CREATE INDEX or ADD CONSTRAINT fails with Msg 1946. A column of a fixed size takes its size in every row, by the model the
/// change brings; a column of a varying width, varchar, nvarchar, varbinary, sql_variant or hierarchyid, takes its DATALENGTH, the value
/// converted by TRY_CONVERT first where the change gives the column another type. A key of one such column counts the rows whose length
/// passes what the fixed columns leave of the limit. A key of two passes the limit by their lengths together, which sets two values of the
/// data against each other, and a key whose fixed columns alone pass the limit fails on an empty table too (Msg 1944); both read not
/// predictable read-only.
/// </summary>
public static partial class AggregateQueries
{
    private static Asked KeyLength(Coordinates.Counted counted, PreconditionKind.KeyLength key, Context context)
    {
        var precondition = counted.Precondition;
        var columns = precondition.Columns.Select(c => context.Column(counted, precondition.Table, c)).ToList();
        if (columns.IndexOf(null) is var missing and >= 0)
        {
            return Asked.Not(Unplaced(precondition.Columns[missing]));
        }

        if (columns.FirstOrDefault(c => c!.After?.KeyBytes is null or long.MaxValue) is { } unsized)
        {
            return Asked.Not("the model gives " + unsized.Place.After + " no type whose size in an index key is known");
        }

        var varying = columns.Where(c => Varying(c!.After!)).Select(c => c!).ToList();
        var fixedBytes = columns.Where(c => !Varying(c!.After!)).Sum(c => c!.After!.KeyBytes!.Value);
        if (fixedBytes > key.Limit)
        {
            return Asked.Not("its columns of a fixed size alone take " + Digits(fixedBytes) + " bytes, past the limit of "
                + Digits(key.Limit) + ", on which SQL Server refuses the index whatever the rows (Msg 1944)");
        }

        if (varying.Count > 1)
        {
            return Asked.Not("its key holds " + Digits(varying.Count) + " columns of a varying width, "
                + string.Join(", ", varying.Select(c => c.Place.After.Name)) + ", and a row passes the limit by their lengths together,"
                + " which sets two values of the data against each other");
        }

        if (varying is not [var column])
        {
            return Part.Presence(precondition);
        }

        if (Operand(column, "t") is not { } value)
        {
            return Asked.Not("a column the change creates has a default that does not parse");
        }

        var converted = column is { Name: not null, Before: { } before, After: { } after } && Written(before) != Written(after)
            && TSql.DataType(Written(after)) is { } target ? new TryConvertCall { DataType = target, Parameter = value } : value;
        return Part.Violating(precondition, Exceeds(Call("DATALENGTH", converted), key.Limit - fixedBytes));
    }

    /// <summary>Whether a type's values take a varying number of bytes in a key: varchar, nvarchar, sysname, varbinary, sql_variant and
    /// hierarchyid.</summary>
    private static bool Varying(ColumnType type) =>
        type.Type.Base.ToLowerInvariant() is "varchar" or "nvarchar" or "sysname" or "varbinary" or "sql_variant" or "hierarchyid";
}

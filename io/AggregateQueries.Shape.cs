using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChange.Io;

/// <summary>
/// The integer queries of WP 3.7's profile, beside the signature form (SqlServer.Signatures.cs): a column's summary, and a table's rows as
/// sys.partitions keeps them, for its row tier. Both pass the allowlist's integer forms as every builder's query does.
/// </summary>
public static partial class AggregateQueries
{
    /// <summary>
    /// A character column's summary (the Facsimile's P6), one SELECT over the table aliased t: its rows; the rows holding NULL; the empty
    /// ones, of no bytes; those of spaces alone, which = N'' holds for, since = ignores trailing spaces; those with a space first or last,
    /// which LIKE reads over a Unicode pattern, where trailing spaces count; its longest value in characters, LEN, and in bytes,
    /// DATALENGTH; and its values distinct as collated and, where its code page has a binary collation (Bin2), distinct as bytes, which is
    /// the last item and left out where it has none.
    /// </summary>
    internal static Result<SqlServer.AggregateQuery> Shape(Name table, string column, string type, string? collation)
    {
        var binary = collation is null ? null : Collation.Of(collation) is Result<Collation>.Ok { Value: var named }
            ? Bin2(named, type is "nchar" or "nvarchar") : null;
        var distinct = Call("COUNT_BIG", ColumnRef("t", column));
        distinct.UniqueRowFilter = UniqueRowFilter.Distinct;
        ScalarExpression[] items =
        [
            CountRows(),
            Sum(Is(ColumnRef("t", column), isNull: true)),
            Sum(Compare(Call("DATALENGTH", ColumnRef("t", column)), BooleanComparisonType.Equals, Number(0))),
            Sum(All([Exceeds(Call("DATALENGTH", ColumnRef("t", column)), 0),
                Compare(ColumnRef("t", column), BooleanComparisonType.Equals, Unicode(""))])),
            Sum(Any([new LikePredicate { FirstExpression = ColumnRef("t", column), SecondExpression = Unicode(" %") },
                new LikePredicate { FirstExpression = ColumnRef("t", column), SecondExpression = Unicode("% ") }])),
            Call("MAX", Call("LEN", ColumnRef("t", column))),
            Call("MAX", Call("DATALENGTH", ColumnRef("t", column))),
            distinct,
            .. binary is { } bytes ? [Distinct("t", column, bytes)] : System.Array.Empty<ScalarExpression>(),
        ];
        return SqlServer.AggregateQuery.Of(Select(items, Named(table, "t")), table + "." + SqlServer.Quoted(column) + " summary");
    }

    /// <summary>
    /// A table's rows from sys.partitions: how many partitions of its heap or clustered index the login sees, and their rows, which a
    /// login holding VIEW DEFINITION and no SELECT reads (§17 item 21); no partition seen is a table it cannot see, never an empty one.
    /// </summary>
    internal static Result<SqlServer.AggregateQuery> PartitionRows(Name table, string site, SqlServer.Session? session)
    {
        var from = Join(Join(Named("sys", "partitions", "p"), Named("sys", "objects", "o"), Equal("o", "object_id", "p", "object_id")),
            Named("sys", "schemas", "s"), Equal("s", "schema_id", "o", "schema_id"));
        var where = All([Compare(ColumnRef("s", "name"), BooleanComparisonType.Equals, Unicode(table.Schema ?? "dbo")),
            Compare(ColumnRef("o", "name"), BooleanComparisonType.Equals, Unicode(table.Base)),
            In(ColumnRef("p", "index_id"), Number(0), Number(1))]);
        return SqlServer.AggregateQuery.Of(Select([CountRows(), Call("SUM", ColumnRef("p", "rows"))], from, where), site, session);
    }
}

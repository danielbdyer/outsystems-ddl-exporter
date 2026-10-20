using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChange.Io;

/// <summary>
/// How a database holds a constraint (spike S9; WP 3.5's trust step): a schema read keeps untrusted only as an internal annotation, so
/// the catalog is read, through SqlServer.Measure, as one of the allowlist's forms, which a login holding VIEW DEFINITION alone reads in a
/// named environment as in a copy.
/// </summary>
public static partial class AggregateQueries
{
    /// <summary>
    /// The aggregate query of a foreign key's or a check's state: over sys.foreign_keys or sys.check_constraints joined to sys.schemas,
    /// the constraint by its schema and its name, how many constraints hold the name, how many of them are disabled, and how many are
    /// untrusted; three integers. A constraint of another type, or one named in one part, as an unnamed constraint is keyed, has none.
    /// </summary>
    public static Result<SqlServer.AggregateQuery>? TrustOf(ElementKey constraint)
    {
        var view = constraint.Type switch
        {
            "ForeignKeyConstraint" => "foreign_keys",
            "CheckConstraint" => "check_constraints",
            _ => null,
        };
        if (view is null || constraint.Parent is not null || constraint.Name.Schema is not { } schema)
        {
            return null;
        }

        var from = Join(Named("sys", view, "c"), Named("sys", "schemas", "s"), Equal("s", "schema_id", "c", "schema_id"));
        var where = All([Compare(ColumnRef("s", "name"), BooleanComparisonType.Equals, Unicode(schema)),
            Compare(ColumnRef("c", "name"), BooleanComparisonType.Equals, Unicode(constraint.Name.Base))]);
        var select = Select([CountRows(), Sum(Compare(ColumnRef("c", "is_disabled"), BooleanComparisonType.Equals, Number(1))),
            Sum(Compare(ColumnRef("c", "is_not_trusted"), BooleanComparisonType.Equals, Number(1)))], from, where);
        return SqlServer.AggregateQuery.Of(select, constraint + ": trust");
    }

    /// <summary>
    /// The state the database holds the constraint in, read by <see cref="TrustOf"/> through SqlServer.Measure; null where it holds no
    /// constraint of that name, or where the constraint has no query. A query SQL Server fails or one past its timeout is server.failed.
    /// </summary>
    public static Result<TrustState?> Trust(SqlServer.Database target, ElementKey constraint, SqlServer.QueryLog log) =>
        TrustOf(constraint) is not { } query ? Result.Ok<TrustState?>(null)
        : query.Bind(admitted => SqlServer.Measure(target, admitted, log)).Bind(measured => measured.Match(
            answered => Result.Ok(answered.Rows is [{ Values: [> 0, var disabled, var untrusted] }]
                ? new TrustState(constraint, disabled > 0, untrusted > 0) : null),
            raised => Result.Fail<TrustState?>(target.ErrorOf(raised.Number, raised.Message ?? "")),
            timedOut => Result.Fail<TrustState?>(target.ErrorOf(-2, "", fatal: false, opened: true))));
}

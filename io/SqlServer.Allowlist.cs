using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnType = Microsoft.SqlServer.TransactSql.ScriptDom.ColumnType;

namespace DbChange.Io;

public static partial class SqlServer
{
    /// <summary>
    /// The aggregate-query allowlist, closed (WP 1.4): one SELECT whose outermost select list holds COUNT or COUNT_BIG of * or of DISTINCT
    /// a column, SUM(CASE WHEN … THEN 1 ELSE 0 END), MIN or MAX over LEN or DATALENGTH of a column, CASE WHEN EXISTS (…) THEN 1 ELSE 0 END
    /// or an integer literal, and, in a query of catalog views alone, SUM of sys.partitions' rows (WP 2.3); beneath it, names of one or two
    /// parts, TRY_ conversions, and the few functions held here. A boundary is a length or a literal, never read from the data: each
    /// predicate, in a join's ON as in a WHERE, reads at most one value from the data (a column, a length or an aggregate), save = or
    /// &lt;&gt; between two columns, an equi-join or an orphan check; and a subquery's select list carries only columns, literals and the
    /// answers above, so a derived column is never two values combined. Every other form is refused with where it stands and what it is,
    /// and none of the query's literals is quoted.
    /// </summary>
    private static class Allowlist
    {
        private const string Forms =
            "COUNT, COUNT_BIG, SUM(CASE WHEN … THEN 1 ELSE 0 END), MIN or MAX over LEN or DATALENGTH of a column, "
                + "CASE WHEN EXISTS or an integer literal";

        private const string Boundary =
            "a boundary read from the data, two values the data holds set against each other; "
                + "= or <> between two columns is the one comparison of two";

        private static readonly HashSet<string> Aggregates = new(StringComparer.OrdinalIgnoreCase) { "COUNT", "COUNT_BIG",
            "SUM", "MIN", "MAX", "AVG" };

        private static readonly HashSet<string> Scalars = new(StringComparer.OrdinalIgnoreCase) { "LEN", "DATALENGTH", "UPPER", "LOWER",
            "LTRIM", "RTRIM", "ISNULL", "ABS" };

        /// <summary>The first form the allowlist does not hold: where it stands, and what it is.</summary>
        private sealed record Offence(TSqlFragment At, string What);

        /// <summary>
        /// The one statement <paramref name="text"/> holds, when the allowlist admits it; a refusal names the line and the column of what
        /// it refuses.
        /// </summary>
        public static Result<TSqlStatement> Admitted(string text)
        {
            var parsed = TSql.Parse(text, out var errors);
            if (errors.Count > 0)
            {
                return new Error("aggregate-query.refused",
                    string.Create(CultureInfo.InvariantCulture,
                        $"The query does not parse at line {errors[0].Line}, column {errors[0].Column}."),
                    "Correct the query's syntax at that place.");
            }

            var statements = ((TSqlScript)parsed).Batches.SelectMany(b => b.Statements).ToList();
            return statements.Count != 1
                ? new Error("aggregate-query.refused",
                    string.Create(CultureInfo.InvariantCulture,
                        $"The query holds {statements.Count} statements; dbchange runs one statement at a time."),
                    "Split it into queries of one SELECT each.")
                : Admitted(statements[0], site: null);
        }

        /// <summary>
        /// A statement as a tree, when the allowlist admits it: what M2's builders make, checked without being written as text and read
        /// again. A tree built in code carries no line and column, so its refusal names <paramref name="site"/> and the kind of node the
        /// allowlist stops at instead.
        /// </summary>
        public static Result<TSqlStatement> Admitted(TSqlStatement statement, string? site) =>
            (statement is SelectStatement select ? Statement(select)
                : new Offence(statement, Kind(statement))) is not { } offence ? statement
            : new Error("aggregate-query.refused", offence.At.StartLine > 0
                    ? string.Create(CultureInfo.InvariantCulture,
                        $"The query is refused at line {offence.At.StartLine}, column {offence.At.StartColumn}: {offence.What}.")
                    : "The query " + site + " is refused at its " + Kind(offence.At) + ": " + offence.What + ".",
                "Rewrite it so its select list holds only " + Forms + ".");

        private static Offence? Statement(SelectStatement s) =>
            s.Into is not null ? new Offence(s.Into, "SELECT INTO")
            : s.WithCtesAndXmlNamespaces is not null ? new Offence(s.WithCtesAndXmlNamespaces, "a WITH clause")
            : s.ComputeClauses.Count > 0 ? new Offence(s.ComputeClauses[0], "COMPUTE")
            : s.OptimizerHints.Count > 0 ? new Offence(s.OptimizerHints[0], "a query hint")
            : s.On is not null ? new Offence(s.On, "ON a filegroup")
            : s.QueryExpression is QuerySpecification q ? Query(q, outermost: true)
            : new Offence(s.QueryExpression, Kind(s.QueryExpression));

        private static Offence? Query(QuerySpecification q, bool outermost) =>
            q.ForClause is not null ? new Offence(q.ForClause, "FOR XML, FOR JSON or FOR BROWSE")
            : q.OrderByClause is not null ? new Offence(q.OrderByClause, "ORDER BY")
            : q.OffsetClause is not null ? new Offence(q.OffsetClause, "OFFSET")
            : q.WindowClause is not null ? new Offence(q.WindowClause, "a WINDOW clause")
            : q.TopRowFilter is { } top && (outermost || top.Percent || top.WithTies || !Literal(top.Expression))
                ? new Offence(top, outermost ? "TOP in the outermost select" : "TOP of other than an integer literal")
            : outermost && q.UniqueRowFilter == UniqueRowFilter.Distinct ? new Offence(q, "DISTINCT in the outermost select")
            : First(q.SelectElements, e => (e, outermost) switch
            {
                (SelectScalarExpression x, true) => PartitionRows(x.Expression, q) ? null : Answer(x.Expression),
                (SelectScalarExpression x, false) => Carried(x.Expression),
                (SelectStarExpression star, false) =>
                    star.Qualifier is null || star.Qualifier.Count <= 2 ? null : Parts(star, star.Qualifier.Count),
                (SelectStarExpression star, true) => new Offence(star, "a star in the outermost select"),
                _ => new Offence(e, "a variable assigned"),
            })
            ?? (q.FromClause is null ? null : First(q.FromClause.TableReferences, Table))
            ?? (q.WhereClause is not { } where ? null : where.Cursor is not null ? new Offence(where, "CURRENT OF")
                : Predicate(where.SearchCondition))
            ?? (q.GroupByClause is not { } group ? null : group.GroupByOption != GroupByOption.None || group.All
                ? new Offence(group, "GROUP BY ALL, ROLLUP or CUBE")
                : First(group.GroupingSpecifications,
                    g => g is ExpressionGroupingSpecification { Expression: var x }
                    && (Length(x) || (!outermost && x is ColumnReferenceExpression c && Column(c) is null))
                    ? null : new Offence(g, outermost
                        ? "GROUP BY other than LEN or DATALENGTH of a column, which draws its groups from the data"
                        : "GROUP BY other than a column or its length")))
            ?? (q.HavingClause is null ? null : Predicate(q.HavingClause.SearchCondition));

        /// <summary>An item of the outermost select list: one of the forms, each answering an integer.</summary>
        private static Offence? Answer(ScalarExpression x) => x switch
        {
            ParenthesisExpression p => Answer(p.Expression),
            IntegerLiteral or UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative, Expression: IntegerLiteral } =>
                long.TryParse(((x as UnaryExpression)?.Expression as IntegerLiteral ?? (IntegerLiteral)x).Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var n) && n <= int.MaxValue
                    ? null : new Offence(x, "an integer literal SQL Server types as numeric"),
            SearchedCaseExpression c when c.WhenClauses is [{ WhenExpression: ExistsPredicate exists }] =>
                OneOrZero(c) ?? Subquery(exists.Subquery.QueryExpression),
            FunctionCall f when Aggregates.Contains(f.FunctionName.Value) => Aggregate(f),
            FunctionCall f => new Offence(f, f.FunctionName.Value.ToUpperInvariant() + ", which the outermost select list does not hold"),
            ColumnReferenceExpression { ColumnType: ColumnType.Regular } => new Offence(x, "a bare column"),
            _ => new Offence(x, Kind(x) + ", which answers other than an integer"),
        };

        /// <summary>
        /// The aggregates the allowlist holds: COUNT or COUNT_BIG of * or of DISTINCT a column; SUM(CASE WHEN … THEN 1 ELSE 0 END); MIN or
        /// MAX over LEN or DATALENGTH of a column.
        /// </summary>
        private static Offence? Aggregate(FunctionCall f)
        {
            var name = f.FunctionName.Value.ToUpperInvariant();
            return f.CallTarget is not null || f.OverClause is not null || f.WithinGroupClause is not null
                ? new Offence(f, name + " with OVER, WITHIN GROUP or a target")
                : (name, f.UniqueRowFilter, f.Parameters) switch
                {
                    ("COUNT" or "COUNT_BIG", UniqueRowFilter.NotSpecified,
                        [ColumnReferenceExpression { ColumnType: ColumnType.Wildcard }]) => null,
                    ("COUNT" or "COUNT_BIG", UniqueRowFilter.Distinct,
                        [ColumnReferenceExpression { ColumnType: ColumnType.Regular } c]) => Column(c),
                    ("COUNT" or "COUNT_BIG", _, _) => new Offence(f, name + " of other than * or DISTINCT a column"),
                    ("SUM", UniqueRowFilter.NotSpecified, [SearchedCaseExpression { WhenClauses: [var when] } c]) =>
                        OneOrZero(c) ?? Predicate(when.WhenExpression),
                    ("SUM", _, _) => new Offence(f, "SUM of other than CASE WHEN … THEN 1 ELSE 0 END"),
                    ("MIN" or "MAX", UniqueRowFilter.NotSpecified, [var length]) when Length(length) => null,
                    (_, _, [ColumnReferenceExpression { ColumnType: ColumnType.Regular }]) => new Offence(f, name + " over a bare column"),
                    _ => new Offence(f, name + " over other than LEN or DATALENGTH of a column"),
                };
        }

        /// <summary>
        /// SUM of the rows column of sys.partitions, in a query whose every table is a catalog view of the sys schema: the row count SQL
        /// Server keeps for each partition, which a login holding VIEW DEFINITION alone reads (§17 item 21), so TableEmpty is read without
        /// a row of the table (WP 2.3). The one form that sums a column; a column of any other table, or sys.partitions beside a table
        /// outside the sys schema, is SUM over a bare column.
        /// </summary>
        private static bool PartitionRows(ScalarExpression x, QuerySpecification q) =>
            x is ParenthesisExpression p ? PartitionRows(p.Expression, q)
            : x is FunctionCall { CallTarget: null, OverClause: null, WithinGroupClause: null,
                UniqueRowFilter: UniqueRowFilter.NotSpecified,
                Parameters: [ColumnReferenceExpression { ColumnType: ColumnType.Regular } column] } sum
            && string.Equals(sum.FunctionName.Value, "SUM", StringComparison.OrdinalIgnoreCase)
            && column.MultiPartIdentifier.Identifiers is { Count: 1 or 2 } parts
            && string.Equals(parts[^1].Value, "rows", StringComparison.OrdinalIgnoreCase)
            && q.FromClause is { } from && Catalog(from.TableReferences) is { Count: > 0 } tables
            && tables.All(t => t.SchemaObject is { ServerIdentifier: null, DatabaseIdentifier: null, SchemaIdentifier.Value: var schema }
                && string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase))
            && tables.Any(t => string.Equals(t.SchemaObject.BaseIdentifier.Value, "partitions", StringComparison.OrdinalIgnoreCase)
                && (parts.Count == 1 ? tables.Count == 1
                    : string.Equals(parts[0].Value, t.Alias?.Value ?? t.SchemaObject.BaseIdentifier.Value,
                        StringComparison.OrdinalIgnoreCase)));

        /// <summary>The tables a FROM clause names, through its joins; null when it holds anything but named tables.</summary>
        private static List<NamedTableReference>? Catalog(IEnumerable<TableReference> references)
        {
            var named = new List<NamedTableReference>();
            foreach (var reference in references)
            {
                var found = reference switch
                {
                    NamedTableReference n => [n],
                    QualifiedJoin j => Catalog([j.FirstTableReference, j.SecondTableReference]),
                    UnqualifiedJoin j => Catalog([j.FirstTableReference, j.SecondTableReference]),
                    JoinParenthesisTableReference p => Catalog([p.Join]),
                    _ => null,
                };
                if (found is null)
                {
                    return null;
                }

                named.AddRange(found);
            }

            return named;
        }

        /// <summary>A CASE whose one WHEN answers 1 and whose ELSE answers 0.</summary>
        private static Offence? OneOrZero(SearchedCaseExpression c) =>
            c.WhenClauses is [{ ThenExpression: IntegerLiteral { Value: "1" } }] && c.ElseExpression is IntegerLiteral { Value: "0" } ? null
                : new Offence(c, "a CASE answering other than 1 or 0");

        /// <summary>
        /// A predicate whose comparisons each read one value from the data, save = or &lt;&gt; of two columns: no row's value bounds
        /// another's, whether a join, a correlated EXISTS or arithmetic (ABS(x - y) + x - y = 0 is x &lt;= y) meets them.
        /// </summary>
        private static Offence? Predicate(BooleanExpression b) => b switch
        {
            BooleanBinaryExpression x => Predicate(x.FirstExpression) ?? Predicate(x.SecondExpression),
            BooleanNotExpression x => Predicate(x.Expression),
            BooleanParenthesisExpression x => Predicate(x.Expression),
            BooleanComparisonExpression x => Scalar(x.FirstExpression) ?? Scalar(x.SecondExpression)
                ?? (x.ComparisonType is BooleanComparisonType.Equals or BooleanComparisonType.NotEqualToBrackets
                    or BooleanComparisonType.NotEqualToExclamation
                    && Bare(x.FirstExpression) && Bare(x.SecondExpression) ? null : Bounded(x, x.FirstExpression, x.SecondExpression)),
            BooleanIsNullExpression x => Scalar(x.Expression) ?? Bounded(x, x.Expression),
            BooleanTernaryExpression x =>
                Scalar(x.FirstExpression) ?? Scalar(x.SecondExpression) ?? Scalar(x.ThirdExpression)
                ?? Bounded(x, x.FirstExpression, x.SecondExpression, x.ThirdExpression),
            LikePredicate x => Scalar(x.FirstExpression) ?? Scalar(x.SecondExpression)
                ?? (x.EscapeExpression is null ? null : Scalar(x.EscapeExpression))
                ?? Bounded(x, x.FirstExpression, x.SecondExpression, x.EscapeExpression),
            InPredicate x => Scalar(x.Expression) ?? (x.Subquery is { } s
                ? Subquery(s.QueryExpression) ?? (Bare(x.Expression) || !Data(x.Expression).Any() ? null : new Offence(x, Boundary))
                : First(x.Values, Scalar) ?? Bounded(x, [x.Expression, .. x.Values])),
            ExistsPredicate x => Subquery(x.Subquery.QueryExpression),
            _ => new Offence(b, Kind(b)),
        };

        /// <summary>
        /// A comparison's operands, which may read one value from the data between them and set it against literals alone.
        /// </summary>
        private static Offence? Bounded(TSqlFragment at, params ScalarExpression?[] operands) =>
            operands.OfType<ScalarExpression>().SelectMany(Data).Skip(1).Any() ? new Offence(at, Boundary) : null;

        /// <summary>
        /// What an operand reads from the data: each column outside a length or an aggregate, each length, each aggregate; a CASE, what its
        /// THEN and ELSE read, its WHEN being a predicate of its own. A literal reads nothing, and any form not named here counts as one
        /// read.
        /// </summary>
        private static IEnumerable<ScalarExpression> Data(ScalarExpression x) => x switch
        {
            _ when Constant(x) => [],
            _ when Length(x) => [x],
            FunctionCall f when Aggregates.Contains(f.FunctionName.Value) => [x],
            UnaryExpression u => Data(u.Expression),
            BinaryExpression y => [.. Data(y.FirstExpression), .. Data(y.SecondExpression)],
            ParenthesisExpression p => Data(p.Expression),
            TryConvertCall t => [.. Data(t.Parameter), .. t.Style is null ? [] : Data(t.Style)],
            TryCastCall t => Data(t.Parameter),
            CoalesceExpression c => c.Expressions.SelectMany(Data),
            NullIfExpression n => [.. Data(n.FirstExpression), .. Data(n.SecondExpression)],
            SearchedCaseExpression s =>
                [.. s.WhenClauses.SelectMany(w => Data(w.ThenExpression)), .. s.ElseExpression is null ? [] : Data(s.ElseExpression)],
            SimpleCaseExpression s =>
                [.. Data(s.InputExpression), .. s.WhenClauses.SelectMany(w => Data(w.WhenExpression).Concat(Data(w.ThenExpression))),
                .. s.ElseExpression is null ? [] : Data(s.ElseExpression)],
            FunctionCall f => f.Parameters.SelectMany(Data),
            _ => [x],
        };

        /// <summary>
        /// An item of a subquery's select list: a column, a literal or one of the answers, so what an outer predicate reads as a column is
        /// a column's own value, a length or a count.
        /// </summary>
        private static Offence? Carried(ScalarExpression x) =>
            x is ParenthesisExpression p ? Carried(p.Expression) : x is ColumnReferenceExpression c ? Column(c)
            : Constant(x) || Answer(x) is null ? null
                : new Offence(x, "a value computed in a subquery's select list, which an outer predicate would read as a column");

        /// <summary>A column as it stands, perhaps in parentheses: one side of an equi-join.</summary>
        private static bool Bare(ScalarExpression x) =>
            x is ColumnReferenceExpression { ColumnType: ColumnType.Regular } || (x is ParenthesisExpression p && Bare(p.Expression));

        /// <summary>A literal of the kinds an aggregate query may write: a number, a string, a binary value or NULL.</summary>
        private static bool Constant(ScalarExpression x) =>
            x is IntegerLiteral or NumericLiteral or RealLiteral or MoneyLiteral or StringLiteral or BinaryLiteral or NullLiteral;

        private static Offence? Scalar(ScalarExpression x) => x switch
        {
            ColumnReferenceExpression c => Column(c),
            _ when Constant(x) => null,
            UnaryExpression u => Scalar(u.Expression),
            BinaryExpression y => Scalar(y.FirstExpression) ?? Scalar(y.SecondExpression),
            ParenthesisExpression p => Scalar(p.Expression),
            TryConvertCall t => Scalar(t.Parameter) ?? (t.Style is null ? null : Scalar(t.Style)),
            TryCastCall t => Scalar(t.Parameter),
            CoalesceExpression c => First(c.Expressions, Scalar),
            NullIfExpression n => Scalar(n.FirstExpression) ?? Scalar(n.SecondExpression),
            SearchedCaseExpression s =>
                First(s.WhenClauses, w => Predicate(w.WhenExpression) ?? Scalar(w.ThenExpression))
                ?? (s.ElseExpression is null ? null : Scalar(s.ElseExpression)),
            SimpleCaseExpression s =>
                Scalar(s.InputExpression) ?? First(s.WhenClauses, w => Scalar(w.WhenExpression) ?? Scalar(w.ThenExpression))
                ?? (s.ElseExpression is null ? null : Scalar(s.ElseExpression)),
            FunctionCall f when Aggregates.Contains(f.FunctionName.Value) => Aggregate(f),
            FunctionCall f when Scalars.Contains(f.FunctionName.Value) && f.CallTarget is null && f.OverClause is null
                && f.UniqueRowFilter == UniqueRowFilter.NotSpecified => First(f.Parameters, Scalar),
            FunctionCall f => new Offence(f,
                (f.CallTarget is null ? f.FunctionName.Value.ToUpperInvariant() : "a function of the database's own")
                + ", which the allowlist does not hold"),
            ConvertCall or CastCall =>
                new Offence(x, "CONVERT or CAST, which quotes the value it fails on; TRY_CONVERT and TRY_CAST do not"),
            ScalarSubquery => new Offence(x, "a subquery's value, a boundary read from the data"),
            VariableReference or GlobalVariableExpression => new Offence(x, "a variable"),
            _ => new Offence(x, Kind(x)),
        };

        private static Offence? Table(TableReference t) => t switch
        {
            NamedTableReference n =>
                n.SchemaObject.ServerIdentifier is not null || n.SchemaObject.DatabaseIdentifier is not null
                ? Parts(n, n.SchemaObject.Count)
                : n.TableHints.Count > 0 ? new Offence(n.TableHints[0], "a table hint")
                : n.TableSampleClause is not null ? new Offence(n.TableSampleClause, "TABLESAMPLE")
                : n.TemporalClause is not null ? new Offence(n.TemporalClause, "FOR SYSTEM_TIME")
                : null,
            QualifiedJoin j => j.JoinHint != JoinHint.None ? new Offence(j, "a join hint")
                : Table(j.FirstTableReference) ?? Table(j.SecondTableReference) ?? Predicate(j.SearchCondition),
            UnqualifiedJoin j => j.UnqualifiedJoinType == UnqualifiedJoinType.CrossJoin
                ? Table(j.FirstTableReference) ?? Table(j.SecondTableReference) : new Offence(j, "CROSS APPLY or OUTER APPLY"),
            JoinParenthesisTableReference p => Table(p.Join),
            QueryDerivedTable d => Subquery(d.QueryExpression),
            _ => new Offence(t, Kind(t)),
        };

        private static Offence? Subquery(QueryExpression q) => q switch
        {
            QuerySpecification s => Query(s, outermost: false),
            QueryParenthesisExpression p => Subquery(p.QueryExpression),
            _ => new Offence(q, Kind(q)),
        };

        private static Offence? Column(ColumnReferenceExpression c) =>
            c.ColumnType != ColumnType.Regular ? new Offence(c, c.ColumnType == ColumnType.Wildcard ? "a star" : "a pseudo-column")
            : c.MultiPartIdentifier.Count <= 2 ? null : Parts(c, c.MultiPartIdentifier.Count);

        /// <summary>LEN or DATALENGTH of a column: a length, the one boundary the allowlist reads from a value.</summary>
        private static bool Length(ScalarExpression x) =>
            x is FunctionCall { CallTarget: null, OverClause: null, UniqueRowFilter: UniqueRowFilter.NotSpecified,
                Parameters: [ColumnReferenceExpression c] } f
            && (string.Equals(f.FunctionName.Value, "LEN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.FunctionName.Value, "DATALENGTH", StringComparison.OrdinalIgnoreCase))
            && Column(c) is null;

        private static bool Literal(ScalarExpression x) => x is IntegerLiteral || x is ParenthesisExpression { Expression: IntegerLiteral };

        private static Offence Parts(TSqlFragment at, int parts) =>
            new(at, string.Create(CultureInfo.InvariantCulture, $"a name of {parts} parts"));

        private static Offence? First<T>(IEnumerable<T> items, Func<T, Offence?> check) =>
            items.Select(check).FirstOrDefault(o => o is not null);

        private static string Kind(TSqlFragment fragment) => fragment.GetType().Name;
    }
}

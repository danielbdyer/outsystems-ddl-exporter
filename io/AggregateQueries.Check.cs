using System;
using System.Collections.Generic;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnKind = Microsoft.SqlServer.TransactSql.ScriptDom.ColumnType;
using ScriptLiteral = Microsoft.SqlServer.TransactSql.ScriptDom.Literal;

namespace DbChange.Io;

/// <summary>
/// Check: the rows where NOT (the check's expression, as the model writes it) holds, which counts false and leaves unknown out, as SQL
/// Server validates a check. The expression reads each column as the environment holds it, one the change creates as the value every
/// existing row holds, and one it converts as TRY_CONVERT to its new type gives it, since DacFx validates the check after the ALTER COLUMN.
/// Each CONVERT or CAST is counted through its TRY_ form, a row on which the conversion fails counted beside the false ones, since SQL
/// Server's validation fails on it; where SQL Server may skip a conversion (under OR, CASE, COALESCE or ISNULL's second argument) the
/// rows it fails on are not known read-only, and the check reads not predictable read-only, as it does for PARSE, which has no TRY_ form
/// the allowlist admits, and for an expression the allowlist refuses.
/// </summary>
public static partial class AggregateQueries
{
    private static Asked Check(Coordinates.Counted counted, PreconditionKind.Check check, Context context)
    {
        var precondition = counted.Precondition;
        var columns = precondition.Columns.Select(c => context.Column(counted, precondition.Table, c)).ToList();
        if (columns.IndexOf(null) is var missing and >= 0)
        {
            return Asked.Not(Unplaced(precondition.Columns[missing]));
        }

        if (TSql.Predicate(check.Expression) is not { } predicate)
        {
            return Asked.Not("its expression does not parse");
        }

        var rewriter = new Rewriter(name => Read(columns.Select(c => c!), name, "t", context, converted: true), conversions: true);
        var rewritten = rewriter.Boolean(predicate, positive: true, every: true);
        return rewriter.Why is { } why ? Asked.Not(why) : Part.Violating(precondition, Any([Not(rewritten), .. rewriter.Failures]));
    }

    /// <summary>An index's filter as the table's own grouping reads it, over its columns unqualified, or as the query over t does; or why
    /// it cannot be read.</summary>
    private static (BooleanExpression? Filter, string? Why) Filtered(string filter, IReadOnlyList<Column> columns, string? alias,
        Context context)
    {
        if (TSql.Predicate(filter) is not { } predicate)
        {
            return (null, "its filter does not parse");
        }

        var rewriter = new Rewriter(name => Read(columns, name, alias, context, converted: false), conversions: false);
        var rewritten = rewriter.Boolean(predicate, positive: true, every: true);
        return rewriter.Why is null ? (rewritten, null) : (null, rewriter.Why);
    }

    /// <summary>
    /// What stands where model text names the column <paramref name="name"/>, matched under the database's collation: the column as the
    /// environment holds it, the value the change gives it, or, where <paramref name="converted"/>, the value TRY_CONVERT to its new type
    /// gives a column the change converts; null for a column the places do not give, or one whose conversion dbchange does not count.
    /// </summary>
    private static ScalarExpression? Read(IEnumerable<Column> columns, string name, string? alias, Context context, bool converted)
    {
        if (columns.FirstOrDefault(c => string.Equals(c.Place.After.Name.Base, name, context.Database.Comparison)) is not { } column)
        {
            return null;
        }

        if (!converted || column is not { Name: { } held, Before: { } before, After: { } after } || before == after)
        {
            return Operand(column, alias);
        }

        var (from, to) = (FamilyOf(before), FamilyOf(after));
        return !Converts(from, to) || TSql.DataType(Written(after)) is not { } type ? null : new TryConvertCall { DataType = type,
            Parameter = ColumnRef(alias, held, from == Family.Character && to == Family.Character ? context.Collation(after) : null) };
    }

    /// <summary>
    /// Model text, a check's expression or an index's filter, rewritten in place as the query reads it: each column replaced by what
    /// stands where it does; and, where conversions are counted, each CONVERT or CAST by its TRY_ form, the rows on which it fails kept as
    /// a failure of their own, which is counted where SQL Server evaluates the conversion on every row: under AND outside any NOT, OR
    /// under one, and the operands of a comparison. A conversion it may skip, PARSE, a subquery, or a column no place gives leaves
    /// <see cref="Why"/>, and the precondition reads not predictable read-only.
    /// </summary>
    private sealed class Rewriter(Func<string, ScalarExpression?> column, bool conversions)
    {
        public List<BooleanExpression> Failures { get; } = [];

        public string? Why { get; private set; }

        /// <summary>A predicate at <paramref name="positive"/> polarity; <paramref name="every"/> says SQL Server evaluates it on every
        /// row, as it does a conjunction's operands, where one false operand decides whatever order SQL Server takes.</summary>
        public BooleanExpression Boolean(BooleanExpression predicate, bool positive, bool every)
        {
            switch (predicate)
            {
                case BooleanBinaryExpression b:
                    var each = every && (b.BinaryExpressionType == BooleanBinaryExpressionType.And) == positive;
                    (b.FirstExpression, b.SecondExpression) =
                        (Boolean(b.FirstExpression, positive, each), Boolean(b.SecondExpression, positive, each));
                    return b;
                case BooleanNotExpression n:
                    n.Expression = Boolean(n.Expression, !positive, every);
                    return n;
                case BooleanParenthesisExpression p:
                    p.Expression = Boolean(p.Expression, positive, every);
                    return p;
                case BooleanComparisonExpression c:
                    (c.FirstExpression, c.SecondExpression) = (Scalar(c.FirstExpression, every), Scalar(c.SecondExpression, every));
                    return c;
                case BooleanIsNullExpression i:
                    i.Expression = Scalar(i.Expression, every);
                    return i;
                case LikePredicate l:
                    (l.FirstExpression, l.SecondExpression) = (Scalar(l.FirstExpression, every), Scalar(l.SecondExpression, every));
                    l.EscapeExpression = l.EscapeExpression is null ? null : Scalar(l.EscapeExpression, every);
                    return l;
                case BooleanTernaryExpression t:
                    var between = (t.TernaryExpressionType == BooleanTernaryExpressionType.Between) == positive;
                    (t.FirstExpression, t.SecondExpression, t.ThirdExpression) = (Scalar(t.FirstExpression, every),
                        Scalar(t.SecondExpression, every && between), Scalar(t.ThirdExpression, every && between));
                    return t;
                case InPredicate { Subquery: null } i:
                    i.Expression = Scalar(i.Expression, every);
                    for (var at = 0; at < i.Values.Count; at++)
                    {
                        i.Values[at] = Scalar(i.Values[at], every && i.NotDefined == positive);
                    }

                    return i;
                default:
                    Why ??= "its expression holds a " + predicate.GetType().Name + ", which dbchange does not read";
                    return predicate;
            }
        }

        /// <summary>A scalar expression; <paramref name="every"/> says SQL Server evaluates it on every row.</summary>
        public ScalarExpression Scalar(ScalarExpression value, bool every)
        {
            switch (value)
            {
                case ColumnReferenceExpression { ColumnType: ColumnKind.Regular } c:
                    var name = c.MultiPartIdentifier.Identifiers[^1].Value;
                    if (column(name) is { } read)
                    {
                        return read;
                    }

                    Why ??= "it reads [" + name + "], which the coordinate rules place nowhere, or converts it in a way dbchange does"
                        + " not count";
                    return value;
                case ScriptLiteral:
                    return value;
                case ParenthesisExpression p:
                    p.Expression = Scalar(p.Expression, every);
                    return p;
                case UnaryExpression u:
                    u.Expression = Scalar(u.Expression, every);
                    return u;
                case BinaryExpression b:
                    (b.FirstExpression, b.SecondExpression) = (Scalar(b.FirstExpression, every), Scalar(b.SecondExpression, every));
                    return b;
                case ConvertCall c:
                    var (operand, style) = (Scalar(c.Parameter, every), c.Style is null ? null : Scalar(c.Style, every));
                    return Tried(operand,
                        parameter => new TryConvertCall { DataType = c.DataType, Parameter = parameter, Style = style }, every);
                case CastCall c:
                    return Tried(Scalar(c.Parameter, every),
                        parameter => new TryCastCall { DataType = c.DataType, Parameter = parameter }, every);
                case TryConvertCall t:
                    t.Parameter = Scalar(t.Parameter, every);
                    return t;
                case TryCastCall t:
                    t.Parameter = Scalar(t.Parameter, every);
                    return t;
                case ParseCall:
                    Why ??= "it calls PARSE, which has no TRY_ form the allowlist admits";
                    return value;
                case CoalesceExpression c:
                    for (var at = 0; at < c.Expressions.Count; at++)
                    {
                        c.Expressions[at] = Scalar(c.Expressions[at], every && at == 0);
                    }

                    return c;
                case NullIfExpression n:
                    (n.FirstExpression, n.SecondExpression) = (Scalar(n.FirstExpression, every), Scalar(n.SecondExpression, every));
                    return n;
                case SearchedCaseExpression s:
                    for (var at = 0; at < s.WhenClauses.Count; at++)
                    {
                        s.WhenClauses[at].WhenExpression = Boolean(s.WhenClauses[at].WhenExpression, true, every && at == 0);
                        s.WhenClauses[at].ThenExpression = Scalar(s.WhenClauses[at].ThenExpression, every: false);
                    }

                    s.ElseExpression = s.ElseExpression is null ? null : Scalar(s.ElseExpression, every: false);
                    return s;
                case SimpleCaseExpression s:
                    s.InputExpression = Scalar(s.InputExpression, every);
                    foreach (var when in s.WhenClauses)
                    {
                        (when.WhenExpression, when.ThenExpression) = (Scalar(when.WhenExpression, every: false),
                            Scalar(when.ThenExpression, every: false));
                    }

                    s.ElseExpression = s.ElseExpression is null ? null : Scalar(s.ElseExpression, every: false);
                    return s;
                case FunctionCall f:
                    var lazy = string.Equals(f.FunctionName.Value, "ISNULL", StringComparison.OrdinalIgnoreCase);
                    for (var at = 0; at < f.Parameters.Count; at++)
                    {
                        f.Parameters[at] = Scalar(f.Parameters[at], every && !(lazy && at > 0));
                    }

                    return f;
                default:
                    return value;
            }
        }

        /// <summary>A conversion's TRY_ form, and the rows on which it fails: those whose value is non-null and whose TRY_ form is
        /// null.</summary>
        private ScalarExpression Tried(ScalarExpression parameter, Func<ScalarExpression, ScalarExpression> tried, bool every)
        {
            if (!conversions || !every)
            {
                Why ??= !conversions ? "its filter converts a value, which a filter counted read-only does not"
                    : "SQL Server may skip a conversion under OR, CASE, COALESCE or ISNULL's second argument, so the rows it fails on"
                        + " are not known read-only";
            }

            var converted = tried(parameter);
            Failures.Add(All([Is(parameter, isNull: false), Is(converted, isNull: true)]));
            return converted;
        }
    }
}

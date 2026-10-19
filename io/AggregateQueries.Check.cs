using System;
using System.Collections.Generic;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnKind = Microsoft.SqlServer.TransactSql.ScriptDom.ColumnType;
using ColumnType = DbChange.Kernel.ColumnType;
using ScriptLiteral = Microsoft.SqlServer.TransactSql.ScriptDom.Literal;

namespace DbChange.Io;

/// <summary>
/// Check: the rows where NOT (the check's expression, as the model writes it) holds, which counts false and leaves unknown out, as SQL
/// Server validates a check. The expression reads each column as the environment holds it, one the change creates as the value every
/// existing row holds, and one it converts as TRY_CONVERT to its new type gives it, since DacFx validates the check after the ALTER COLUMN.
/// Every conversion is counted through its TRY_ form, a row on which it fails counted beside the false ones, since SQL Server's validation
/// fails on it: each CONVERT or CAST, and each string SQL Server converts with no CONVERT written where it meets a number, a date and time
/// type or uniqueidentifier (Types.cs), which would otherwise raise Msg 245 quoting the row's value. Where SQL Server may skip a
/// conversion (under OR, CASE, COALESCE or ISNULL's second argument) the rows it fails on are not known read-only, and the check reads not
/// predictable read-only, as it does for PARSE, which has no TRY_ form the allowlist admits, for an operand whose type dbchange does not
/// read beside one SQL Server may convert, and for an expression the allowlist refuses.
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
    /// What stands where model text names the column <paramref name="name"/>, matched under the database's collation, and its type there:
    /// the column as the environment holds it, the value the change gives it, or, where <paramref name="converted"/>, the value TRY_CONVERT
    /// to its new type gives a column the change converts; null for a column the places do not give, or one whose conversion dbchange does
    /// not count.
    /// </summary>
    private static (ScalarExpression Value, ColumnType? Type)? Read(IEnumerable<Column> columns, string name, string? alias,
        Context context, bool converted)
    {
        if (columns.FirstOrDefault(c => string.Equals(c.Place.After.Name.Base, name, context.Database.Comparison)) is not { } column)
        {
            return null;
        }

        if (!converted || column is not { Name: { } held, Before: { } before, After: { } after } || before == after)
        {
            return Operand(column, alias) is { } value
                ? (value, converted || column.Name is null ? column.After ?? column.Before : column.Before) : null;
        }

        var (from, to) = (FamilyOf(before), FamilyOf(after));
        return !Converts(from, to) || TSql.DataType(Written(after)) is not { } type ? null
            : (new TryConvertCall { DataType = type, Parameter = ColumnRef(alias, held,
                from == Family.Character && to == Family.Character ? context.Collation(after) : null) }, after);
    }

    /// <summary>
    /// Model text, a check's expression or an index's filter, rewritten in place as the query reads it: each column replaced by what
    /// stands where it does; and, where conversions are counted, each conversion by its TRY_ form, the rows on which it fails kept as a
    /// failure of their own, which is counted where SQL Server evaluates the conversion on every row: under AND outside any NOT, OR under
    /// one, and the operands of a comparison. A conversion it may skip, PARSE, a subquery, a column no place gives, or an operand whose
    /// type is not read beside one SQL Server may convert leaves <see cref="Why"/>, and the precondition reads not predictable read-only.
    /// </summary>
    private sealed class Rewriter(Func<string, (ScalarExpression Value, ColumnType? Type)?> column, bool conversions)
    {
        /// <summary>The type of each operand the rewriter wrote or combined; null where it reads none.</summary>
        private readonly Dictionary<ScalarExpression, Typed?> types = new(ReferenceEqualityComparer.Instance);

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
                    var first = Scalar(c.FirstExpression, every);
                    (c.FirstExpression, var second) = Compared(first, [Scalar(c.SecondExpression, every)], every, every);
                    c.SecondExpression = second[0];
                    return c;
                case BooleanIsNullExpression i:
                    i.Expression = Scalar(i.Expression, every);
                    return i;
                case LikePredicate l:
                    // LIKE converts both operands to strings, which never fails.
                    (l.FirstExpression, l.SecondExpression) = (Scalar(l.FirstExpression, every), Scalar(l.SecondExpression, every));
                    l.EscapeExpression = l.EscapeExpression is null ? null : Scalar(l.EscapeExpression, every);
                    return l;
                case BooleanTernaryExpression t:
                    var between = (t.TernaryExpressionType == BooleanTernaryExpressionType.Between) == positive;
                    var tested = Scalar(t.FirstExpression, every);
                    var (low, high) = (Scalar(t.SecondExpression, every && between), Scalar(t.ThirdExpression, every && between));
                    (t.FirstExpression, var bounds) = Compared(tested, [low, high], every, every && between);
                    (t.SecondExpression, t.ThirdExpression) = (bounds[0], bounds[1]);
                    return t;
                case InPredicate { Subquery: null } i:
                    var listed = every && i.NotDefined == positive;
                    var member = Scalar(i.Expression, every);
                    (i.Expression, var values) = Compared(member, [.. i.Values.Select(v => Scalar(v, listed))], every, listed);
                    for (var at = 0; at < values.Length; at++)
                    {
                        i.Values[at] = values[at];
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
                        types[read.Value] = read.Type is { } type ? Typed.Of(type) : null;
                        return read.Value;
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
                    return Arithmetic(b, every);
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
                    // COALESCE evaluates an argument only on the rows whose arguments before it are NULL.
                    var (coalesced, common) = Common([.. c.Expressions.Select((e, at) => Scalar(e, every && at == 0))],
                        at => every && at == 0);
                    for (var at = 0; at < coalesced.Length; at++)
                    {
                        c.Expressions[at] = coalesced[at];
                    }

                    types[c] = common;
                    return c;
                case NullIfExpression n:
                    return NullIf(n, every);
                case SearchedCaseExpression s:
                    for (var at = 0; at < s.WhenClauses.Count; at++)
                    {
                        s.WhenClauses[at].WhenExpression = Boolean(s.WhenClauses[at].WhenExpression, true, every && at == 0);
                        s.WhenClauses[at].ThenExpression = Scalar(s.WhenClauses[at].ThenExpression, every: false);
                    }

                    s.ElseExpression = s.ElseExpression is null ? null : Scalar(s.ElseExpression, every: false);
                    types[s] = Results(s.WhenClauses, s.ElseExpression, e => s.ElseExpression = e);
                    return s;
                case SimpleCaseExpression s:
                    var input = Scalar(s.InputExpression, every);
                    foreach (var when in s.WhenClauses)
                    {
                        (when.WhenExpression, when.ThenExpression) = (Scalar(when.WhenExpression, every: false),
                            Scalar(when.ThenExpression, every: false));
                    }

                    s.ElseExpression = s.ElseExpression is null ? null : Scalar(s.ElseExpression, every: false);
                    (s.InputExpression, var whens) = Compared(input, [.. s.WhenClauses.Select(w => w.WhenExpression)], every, false);
                    for (var at = 0; at < whens.Length; at++)
                    {
                        s.WhenClauses[at].WhenExpression = whens[at];
                    }

                    types[s] = Results(s.WhenClauses, s.ElseExpression, e => s.ElseExpression = e);
                    return s;
                case FunctionCall f:
                    var lazy = string.Equals(f.FunctionName.Value, "ISNULL", StringComparison.OrdinalIgnoreCase);
                    for (var at = 0; at < f.Parameters.Count; at++)
                    {
                        f.Parameters[at] = Scalar(f.Parameters[at], every && !(lazy && at > 0));
                    }

                    types[f] = Function(f);
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

        /// <summary>The type an operand of the rewritten text has, or null where the rewriter does not read it.</summary>
        private Typed? TypeOf(ScalarExpression value) => types.TryGetValue(value, out var known) ? known : value switch
        {
            ScriptLiteral literal => Typed.Of(literal),
            ParenthesisExpression p => TypeOf(p.Expression),
            UnaryExpression u => TypeOf(u.Expression),
            TryConvertCall t => Typed.Of(t.DataType),
            TryCastCall t => Typed.Of(t.DataType),
            _ => null,
        };

        /// <summary>
        /// A comparison of <paramref name="subject"/> with each of <paramref name="others"/> (an operator's other operand, BETWEEN's
        /// bounds, IN's values, a simple CASE's WHENs), each string SQL Server converts written as TRY_CONVERT to the type it converts to:
        /// the subject, where it is a string and every other it meets is of one type that takes strings; each other that is a string,
        /// where the subject is of such a type. SQL Server converting a string to more than one type, or an operand whose type is not
        /// read, leaves <see cref="Why"/>. <paramref name="every"/> and <paramref name="everyOther"/> say whether SQL Server evaluates the
        /// subject and the others on every row.
        /// </summary>
        private (ScalarExpression Subject, ScalarExpression[] Others) Compared(ScalarExpression subject, ScalarExpression[] others,
            bool every, bool everyOther)
        {
            var (type, typed) = (TypeOf(subject), others.Select(TypeOf).ToList());
            if (type is null || typed.Contains(null))
            {
                Why ??= "it compares a value whose type dbchange does not read, which SQL Server may convert with no CONVERT written";
                return (subject, others);
            }

            var met = typed.Where(t => t != Typed.Null).Distinct().ToList();
            if (type.IsString && met.Any(t => t!.TakesStrings))
            {
                if (met is [{ } one])
                {
                    return (Converted(subject, one, every), others);
                }

                Why ??= "it compares a string with values of more than one type, and SQL Server converts the string to each in turn";
                return (subject, others);
            }

            return (subject, type.TakesStrings ? [.. others.Select((o, at) => typed[at]!.IsString ? Converted(o, type, everyOther) : o)]
                : others);
        }

        /// <summary>
        /// The values SQL Server gives one type, COALESCE's arguments or a CASE's results: the type of highest precedence among them, read
        /// exactly where no two of the values that are not strings differ; each string among them converted to it where it takes strings,
        /// <paramref name="every"/> saying of each whether SQL Server evaluates it on every row. Null for the type where one is not read.
        /// </summary>
        private (ScalarExpression[] Values, Typed? Type) Common(ScalarExpression[] values, Func<int, bool> every)
        {
            var typed = values.Select(TypeOf).ToList();
            if (typed.Contains(null))
            {
                Why ??= "it combines a value whose type dbchange does not read, which SQL Server may convert with no CONVERT written";
                return (values, null);
            }

            var highest = typed.MaxBy(t => t!.Rank)!;
            var exact = typed.Where(t => t != Typed.Null && !t!.IsString).Distinct().Count() <= 1;
            return (highest.TakesStrings ? [.. values.Select((v, at) => typed[at]!.IsString ? Converted(v, highest, every(at)) : v)]
                : values, exact ? highest : highest with { Target = null });
        }

        /// <summary>A CASE's results, its THENs and its ELSE, given one type; SQL Server evaluates each only on the rows that reach
        /// it.</summary>
        private Typed? Results<TWhen>(IList<TWhen> whens, ScalarExpression? otherwise, Action<ScalarExpression> elseIs)
            where TWhen : WhenClause
        {
            var (values, type) = Common([.. whens.Select(w => w.ThenExpression), .. otherwise is null ? [] : new[] { otherwise }],
                _ => false);
            for (var at = 0; at < whens.Count; at++)
            {
                whens[at].ThenExpression = values[at];
            }

            if (otherwise is not null)
            {
                elseIs(values[^1]);
            }

            return type;
        }

        /// <summary>A sum, a difference, a product or a quotient: a concatenation where both operands are strings, which converts neither;
        /// else the string operand converted to the other's type.</summary>
        private BinaryExpression Arithmetic(BinaryExpression b, bool every)
        {
            var (left, right) = (Scalar(b.FirstExpression, every), Scalar(b.SecondExpression, every));
            if (b.BinaryExpressionType == BinaryExpressionType.Add
                && TypeOf(left) is { IsString: true } && TypeOf(right) is { IsString: true })
            {
                (b.FirstExpression, b.SecondExpression) = (left, right);
                types[b] = Typed.String;
                return b;
            }

            (b.FirstExpression, var operand) = Compared(left, [right], every, every);
            b.SecondExpression = operand[0];
            types[b] = Typed.Arithmetic(TypeOf(b.FirstExpression), TypeOf(b.SecondExpression));
            return b;
        }

        /// <summary>NULLIF(a, b), which compares a with b and answers a: a string a that SQL Server converts to b's type is not read, since
        /// its TRY_ form would change what NULLIF answers.</summary>
        private NullIfExpression NullIf(NullIfExpression n, bool every)
        {
            var (first, second) = (Scalar(n.FirstExpression, every), Scalar(n.SecondExpression, every));
            if (TypeOf(first) is { IsString: true } && TypeOf(second) is { TakesStrings: true })
            {
                Why ??= "NULLIF compares a string with a value SQL Server converts it to, and answers the string";
            }

            (n.FirstExpression, var compared) = Compared(first, [second], every, every);
            n.SecondExpression = compared[0];
            types[n] = TypeOf(first);
            return n;
        }

        /// <summary>
        /// The type of a call the allowlist admits: LEN and DATALENGTH an int; UPPER, LOWER, LTRIM and RTRIM a string; ABS its argument's;
        /// ISNULL its first argument's, its second converted to it where that is a string and the first takes strings, which SQL Server
        /// does only on the rows whose first is NULL. Null for any other call, and for ABS of a string, which SQL Server converts to float.
        /// </summary>
        private Typed? Function(FunctionCall f)
        {
            var first = f.Parameters.Count > 0 ? TypeOf(f.Parameters[0]) : null;
            switch (f.FunctionName.Value.ToUpperInvariant())
            {
                case "LEN" or "DATALENGTH":
                    return Typed.Int;
                case "UPPER" or "LOWER" or "LTRIM" or "RTRIM":
                    return Typed.String;
                case "ABS" when first is { IsString: true }:
                    Why ??= "it calls ABS on a string, which SQL Server converts to float with no CONVERT written";
                    return null;
                case "ABS":
                    return first;
                case "ISNULL" when f.Parameters.Count == 2:
                    var second = TypeOf(f.Parameters[1]);
                    if (first is { TakesStrings: true } && second is { IsString: true })
                    {
                        f.Parameters[1] = Converted(f.Parameters[1], first, every: false);
                    }

                    return first == Typed.Null ? second : first;
                default:
                    return null;
            }
        }

        /// <summary>A string SQL Server converts to <paramref name="target"/> with no CONVERT written, as TRY_CONVERT to that type, the
        /// rows on which it fails counted as <see cref="Tried"/> counts them.</summary>
        private ScalarExpression Converted(ScalarExpression value, Typed target, bool every)
        {
            if (target.Target is not { } written || TSql.DataType(written) is not { } type)
            {
                Why ??= "SQL Server converts a string in it to " + target.Base + ", of a precision dbchange does not read";
                return value;
            }

            var converted = Tried(value, parameter => new TryConvertCall { DataType = type, Parameter = parameter }, every);
            types[converted] = target;
            return converted;
        }
    }
}

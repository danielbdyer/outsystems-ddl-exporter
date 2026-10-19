using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnKind = Microsoft.SqlServer.TransactSql.ScriptDom.ColumnType;

namespace DbChange.Io;

/// <summary>
/// The ScriptDom trees the builders write: every name in brackets, every literal typed, and nothing read back from text but the model's
/// own, a check's expression, an index's filter, a default and a type.
/// </summary>
public static partial class AggregateQueries
{
    private static Identifier Id(string value) => new() { Value = value, QuoteType = QuoteType.SquareBracket };

    private static ColumnReferenceExpression ColumnRef(string? qualifier, string name, string? collation = null)
    {
        var column = new ColumnReferenceExpression { ColumnType = ColumnKind.Regular, MultiPartIdentifier = new MultiPartIdentifier(),
            Collation = collation is null ? null : new Identifier { Value = collation } };
        foreach (var part in qualifier is null ? [name] : new[] { qualifier, name })
        {
            column.MultiPartIdentifier.Identifiers.Add(Id(part));
        }

        return column;
    }

    private static IntegerLiteral Number(long n) => new() { Value = n.ToString(CultureInfo.InvariantCulture) };

    private static StringLiteral Unicode(string text) => new() { Value = text, IsNational = true };

    private static FunctionCall Call(string name, params ScalarExpression[] parameters)
    {
        var call = new FunctionCall { FunctionName = new Identifier { Value = name } };
        foreach (var parameter in parameters)
        {
            call.Parameters.Add(parameter);
        }

        return call;
    }

    private static FunctionCall CountRows() => Call("COUNT_BIG", new ColumnReferenceExpression { ColumnType = ColumnKind.Wildcard });

    /// <summary>SUM(CASE WHEN <paramref name="when"/> THEN 1 ELSE 0 END): the rows a predicate holds for.</summary>
    private static FunctionCall Sum(BooleanExpression when) => Call("SUM", new SearchedCaseExpression
    {
        WhenClauses = { new SearchedWhenClause { WhenExpression = when, ThenExpression = Number(1) } },
        ElseExpression = Number(0),
    });

    private static BooleanIsNullExpression Is(ScalarExpression value, bool isNull) => new() { Expression = value, IsNot = !isNull };

    private static BooleanComparisonExpression Compare(ScalarExpression first, BooleanComparisonType comparison, ScalarExpression second) =>
        new() { FirstExpression = first, ComparisonType = comparison, SecondExpression = second };

    private static BooleanComparisonExpression Equal(string qualifier, string column, string otherQualifier, string otherColumn) =>
        Compare(ColumnRef(qualifier, column), BooleanComparisonType.Equals, ColumnRef(otherQualifier, otherColumn));

    /// <summary><paramref name="value"/> &gt; <paramref name="n"/>.</summary>
    private static BooleanComparisonExpression Exceeds(ScalarExpression value, long n) =>
        Compare(value, BooleanComparisonType.GreaterThan, Number(n));

    private static InPredicate In(ScalarExpression value, params ScalarExpression[] values)
    {
        var predicate = new InPredicate { Expression = value };
        foreach (var each in values)
        {
            predicate.Values.Add(each);
        }

        return predicate;
    }

    /// <summary>The predicates joined by AND, or by OR; an operand that joins its own is put in parentheses, as the generator writes none.
    /// </summary>
    private static BooleanExpression All(IEnumerable<BooleanExpression> predicates) => Joined(predicates, BooleanBinaryExpressionType.And);

    private static BooleanExpression Any(IEnumerable<BooleanExpression> predicates) => Joined(predicates, BooleanBinaryExpressionType.Or);

    private static BooleanExpression Joined(IEnumerable<BooleanExpression> predicates, BooleanBinaryExpressionType type) =>
        predicates.Select(p => p is BooleanBinaryExpression ? new BooleanParenthesisExpression { Expression = p } : p)
            .Aggregate((left, right) =>
                new BooleanBinaryExpression { BinaryExpressionType = type, FirstExpression = left, SecondExpression = right });

    private static BooleanNotExpression Not(BooleanExpression predicate) =>
        new() { Expression = new BooleanParenthesisExpression { Expression = predicate } };

    private static NamedTableReference Named(Name table, string? alias) => Named(table.Schema, table.Base, alias);

    private static NamedTableReference Named(string? schema, string name, string? alias)
    {
        var named = new NamedTableReference { SchemaObject = new SchemaObjectName(), Alias = alias is null ? null : Id(alias) };
        foreach (var part in schema is null ? [name] : new[] { schema, name })
        {
            named.SchemaObject.Identifiers.Add(Id(part));
        }

        return named;
    }

    private static QualifiedJoin Join(TableReference left, TableReference right, BooleanExpression on) =>
        new()
        {
            QualifiedJoinType = QualifiedJoinType.Inner, FirstTableReference = left, SecondTableReference = right, SearchCondition = on,
        };

    /// <summary>A SELECT of the items given, each named when a name is given, from the table given, with a WHERE and a GROUP BY when given.
    /// </summary>
    private static SelectStatement Select(IEnumerable<ScalarExpression> items, TableReference from, BooleanExpression? where = null) =>
        new() { QueryExpression = Query(items.Select(i => (i, (string?)null)), from, where, []) };

    private static QuerySpecification Query(IEnumerable<(ScalarExpression Item, string? Name)> items, TableReference from,
        BooleanExpression? where, IEnumerable<ScalarExpression> groups)
    {
        var query = new QuerySpecification { FromClause = new FromClause { TableReferences = { from } },
            WhereClause = where is null ? null : new WhereClause { SearchCondition = where } };
        foreach (var (item, name) in items)
        {
            query.SelectElements.Add(new SelectScalarExpression { Expression = item,
                ColumnName = name is null ? null : new IdentifierOrValueExpression { Identifier = Id(name) } });
        }

        foreach (var group in groups)
        {
            query.GroupByClause ??= new GroupByClause();
            query.GroupByClause.GroupingSpecifications.Add(new ExpressionGroupingSpecification { Expression = group });
        }

        return query;
    }

    private static QueryDerivedTable Derived(QuerySpecification query, string alias) =>
        new() { QueryExpression = query, Alias = Id(alias) };
}

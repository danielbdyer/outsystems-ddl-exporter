using System;
using System.Collections.Generic;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ScriptLiteral = Microsoft.SqlServer.TransactSql.ScriptDom.Literal;

namespace DbChange.Io;

/// <summary>
/// Where a declared block makes text or bytes of a number (VALUES.md P1). A number carries no mark (PLT-124) and is admitted where it
/// stays a number, but SQL Server stores a number written as a character column's value as its digits, so a phone or a card number
/// written without its quotes would reach the copy and the committed file as text. A number is held to its digits being a literal the
/// repository's model defines where it is the value a statement writes into a character or binary column of the copy (an INSERT's, by
/// its column list or, with none, by the table's columns in order; an UPDATE's or a MERGE's SET; an OUTPUT INTO's), written itself,
/// inside parentheses, a sign, a CAST or a CONVERT, as a scalar subquery's value, or through a column of a VALUES list, a derived table
/// or a common table expression the statement reads; and where it is the operand of a CAST, CONVERT, TRY_CAST or TRY_CONVERT to a
/// character or binary type, an argument of CONCAT or CONCAT_WS, or the first of STR or FORMAT, wherever it stands. Not read: a number
/// computed by arithmetic, a CASE or another function; a character CHAR or NCHAR makes of a code; a column of a view or of anything
/// else the copy's tables do not hold; a conversion to an alias type.
/// </summary>
public static partial class Witnesses
{
    /// <summary>SQL Server's character and binary types, by the names the model gives them, sysname among them.</summary>
    private static readonly HashSet<string> CharacterOrBinary = new(StringComparer.OrdinalIgnoreCase)
    {
        "char", "varchar", "nchar", "nvarchar", "text", "ntext", "sysname", "binary", "varbinary", "image",
    };

    /// <summary>
    /// The first number, by its place in the statement, that the statement makes text or bytes of as the class says and whose digits
    /// the repository's model does not define as a literal; null where there is none.
    /// </summary>
    private static ScriptLiteral? NumberAsText(TSqlStatement statement, Repository repository)
    {
        var numbers = new Numbers(repository, statement is StatementWithCtesAndXmlNamespaces { WithCtesAndXmlNamespaces: { } common }
            ? [.. common.CommonTableExpressions] : []);
        numbers.Read(statement);
        statement.Accept(numbers.Conversions());
        return numbers.Found.Where(n => !repository.Literals.Contains(n.Value)).OrderBy(n => n.StartOffset).FirstOrDefault();
    }

    /// <summary>A row of values by position, with the sources its column references read.</summary>
    private sealed record Row(IReadOnlyList<ScalarExpression?> Values, IReadOnlyList<Source> Scope);

    /// <summary>What a column reference can read: a VALUES list, a derived table or a common table expression, by its alias, its
    /// columns' names and its rows.</summary>
    private sealed record Source(string? Alias, IReadOnlyList<string?> Columns, IReadOnlyList<Row> Rows);

    /// <summary>The numbers a statement makes text or bytes of, found as the class says.</summary>
    private sealed class Numbers(Repository repository, IReadOnlyList<CommonTableExpression> ctes)
    {
        /// <summary>How deep a value is followed through derived tables and common table expressions, which may read
        /// themselves.</summary>
        private const int Deepest = 8;

        public List<ScriptLiteral> Found { get; } = [];

        /// <summary>The visitor that finds each conversion's operand, wherever it stands.</summary>
        public TSqlFragmentVisitor Conversions() => new Converted(operand => Found.AddRange(Of(operand, [], 0)));

        /// <summary>The values a statement writes, each held to its column's type.</summary>
        public void Read(TSqlStatement statement)
        {
            switch (statement)
            {
                case InsertStatement { InsertSpecification: var insert }:
                    Insert(Target(insert.Target, []), insert.Columns, insert.InsertSource, []);
                    Output(insert.OutputIntoClause);
                    break;
                case UpdateStatement { UpdateSpecification: var update }:
                    IList<TableReference> joined = update.FromClause?.TableReferences ?? [];
                    Set(Target(update.Target, joined), update.SetClauses, Sources(joined, 0));
                    Output(update.OutputIntoClause);
                    break;
                case MergeStatement { MergeSpecification: var merge }:
                    var (target, source) = (Target(merge.Target, []), Sources([merge.TableReference], 0));
                    foreach (var action in merge.ActionClauses.Select(c => c.Action))
                    {
                        if (action is InsertMergeAction inserted)
                        {
                            Insert(target, inserted.Columns, inserted.Source, source);
                        }
                        else if (action is UpdateMergeAction updated)
                        {
                            Set(target, updated.SetClauses, source);
                        }
                    }

                    Output(merge.OutputIntoClause);
                    break;
                case DeleteStatement { DeleteSpecification: var delete }:
                    Output(delete.OutputIntoClause);
                    break;
            }
        }

        private void Insert(Table? table, IList<ColumnReferenceExpression> columns, InsertSource? source, IReadOnlyList<Source> scope) =>
            Write(table, columns.Count > 0 ? [.. columns.Select(Last)] : Insertable(table), source switch
            {
                ValuesInsertSource values => [.. values.RowValues.Select(r => new Row([.. r.ColumnValues], scope))],
                SelectInsertSource select => Rows(select.Select, 0),
                _ => [],
            });

        private void Set(Table? table, IList<SetClause> clauses, IReadOnlyList<Source> scope)
        {
            foreach (var set in clauses.OfType<AssignmentSetClause>().Where(s => s.Column is not null))
            {
                Write(table, [Last(set.Column)], [new Row([set.NewValue], scope)]);
            }
        }

        private void Output(OutputIntoClause? output)
        {
            if (output is not null && Target(output.IntoTable, []) is { } table)
            {
                Write(table, output.IntoTableColumns.Count > 0 ? [.. output.IntoTableColumns.Select(Last)] : Insertable(table),
                    [new Row([.. output.SelectColumns.Select(c => (c as SelectScalarExpression)?.Expression)], [])]);
            }
        }

        /// <summary>Each row's value at a column of a character or binary type, followed to the numbers it is.</summary>
        private void Write(Table? table, IReadOnlyList<string?> columns, IReadOnlyList<Row> rows)
        {
            if (table is null)
            {
                return;
            }

            foreach (var (column, at) in columns.Select((name, i) => (name, i)))
            {
                if (table.Columns.FirstOrDefault(c => Same(c.Name.Base, column)) is Column.Stored { Type: var type }
                    && CharacterOrBinary.Contains(type.Type.Base))
                {
                    Found.AddRange(rows.Where(r => at < r.Values.Count).SelectMany(r => Of(r.Values[at], r.Scope, 0)));
                }
            }
        }

        /// <summary>
        /// The copy's table a statement writes, as the statement names it: in two parts, or in one, as the alias of a table its FROM names
        /// or as a table of dbo; null for any other target, a view, a common table expression or a table the copy does not hold.
        /// </summary>
        private Table? Target(TableReference? target, IList<TableReference> joined)
        {
            if (target is not NamedTableReference { SchemaObject: var name })
            {
                return null;
            }

            name = name.SchemaIdentifier is null && Aliased(joined, name.BaseIdentifier.Value) is { } aliased ? aliased : name;
            var (schema, table) = (name.SchemaIdentifier?.Value ?? "dbo", name.BaseIdentifier.Value);
            return name.DatabaseIdentifier is not null ? null
                : repository.Copy.Tables.FirstOrDefault(t => Same(t.Name.Schema, schema) && Same(t.Name.Base, table));
        }

        /// <summary>The columns an INSERT that lists none writes: each stored column of the table in order but an identity and a
        /// rowversion, which SQL Server fills.</summary>
        private static IReadOnlyList<string?> Insertable(Table? table) => table is null ? []
            : [.. table.Columns.OfType<Column.Stored>().Where(c => c.Identity is null && !Same(c.Type.Type.Base, "timestamp")
                && !Same(c.Type.Type.Base, "rowversion")).Select(c => (string?)c.Name.Base)];

        /// <summary>The sources a FROM or a MERGE's USING names, through its joins: a VALUES list, a derived table, and a common table
        /// expression of the statement, each by its alias.</summary>
        private List<Source> Sources(IEnumerable<TableReference> references, int depth) => [.. references.SelectMany(r => r switch
        {
            JoinTableReference join => Sources([join.FirstTableReference, join.SecondTableReference], depth),
            JoinParenthesisTableReference parenthesis => Sources([parenthesis.Join], depth),
            InlineDerivedTable values => [new Source(values.Alias?.Value, [.. values.Columns.Select(c => (string?)c.Value)],
                [.. values.RowValues.Select(v => new Row([.. v.ColumnValues], []))])],
            QueryDerivedTable query => [new Source(query.Alias?.Value,
                query.Columns.Count > 0 ? [.. query.Columns.Select(c => (string?)c.Value)] : Names(query.QueryExpression),
                Rows(query.QueryExpression, depth + 1))],
            NamedTableReference { SchemaObject: { SchemaIdentifier: null, BaseIdentifier.Value: var name } } named
                when ctes.FirstOrDefault(c => Same(c.ExpressionName.Value, name)) is { } cte =>
                [new Source(named.Alias?.Value ?? name,
                    cte.Columns.Count > 0 ? [.. cte.Columns.Select(c => (string?)c.Value)] : Names(cte.QueryExpression),
                    Rows(cte.QueryExpression, depth + 1))],
            _ => [],
        })];

        /// <summary>The rows a query returns, each of its select list's values with the sources its FROM names; none for a select list
        /// holding a *, whose columns stand where the query does not say.</summary>
        private IReadOnlyList<Row> Rows(QueryExpression? query, int depth) => depth > Deepest ? [] : query switch
        {
            QuerySpecification select when select.SelectElements.All(e => e is SelectScalarExpression) =>
                [new Row([.. select.SelectElements.Cast<SelectScalarExpression>().Select(e => e.Expression)],
                    Sources(select.FromClause?.TableReferences ?? [], depth))],
            BinaryQueryExpression union => [.. Rows(union.FirstQueryExpression, depth), .. Rows(union.SecondQueryExpression, depth)],
            QueryParenthesisExpression parenthesis => Rows(parenthesis.QueryExpression, depth),
            _ => [],
        };

        /// <summary>The numbers a value is: a number itself, inside parentheses, a sign, a CAST or a CONVERT; a scalar subquery's value;
        /// or a column of a source in scope, each of its rows' values there.</summary>
        private IEnumerable<ScriptLiteral> Of(ScalarExpression? value, IReadOnlyList<Source> scope, int depth) => depth > Deepest ? []
            : value switch
            {
                IntegerLiteral or NumericLiteral or RealLiteral or MoneyLiteral => [(ScriptLiteral)value],
                ParenthesisExpression parenthesis => Of(parenthesis.Expression, scope, depth),
                UnaryExpression signed => Of(signed.Expression, scope, depth),
                CastCall cast => Of(cast.Parameter, scope, depth),
                ConvertCall convert => Of(convert.Parameter, scope, depth),
                TryCastCall cast => Of(cast.Parameter, scope, depth),
                TryConvertCall convert => Of(convert.Parameter, scope, depth),
                ScalarSubquery subquery => Rows(subquery.QueryExpression, depth + 1)
                    .SelectMany(r => r.Values.Count > 0 ? Of(r.Values[0], r.Scope, depth + 1) : []),
                ColumnReferenceExpression column when Resolved(column, scope) is ({ } source, var at) =>
                    source.Rows.SelectMany(r => at < r.Values.Count ? Of(r.Values[at], r.Scope, depth + 1) : []),
                _ => [],
            };

        /// <summary>The source a column reference reads and the column's place in it: the source its qualifier names, or, unqualified,
        /// the one source in scope that has the column.</summary>
        private static (Source? Source, int At) Resolved(ColumnReferenceExpression column, IReadOnlyList<Source> scope)
        {
            if (column.MultiPartIdentifier?.Identifiers is not { Count: > 0 } parts)
            {
                return (null, -1);
            }

            var named = scope.Where(s => parts.Count == 1 || Same(s.Alias, parts[^2].Value))
                .Select(s => (Source: s, At: s.Columns.ToList().FindIndex(c => Same(c, parts[^1].Value)))).Where(s => s.At >= 0).ToList();
            return named is [var one] ? (one.Source, one.At) : (null, -1);
        }

        /// <summary>The names of a query's columns: each value's alias, or the column it reads.</summary>
        private static IReadOnlyList<string?> Names(QueryExpression? query) => query switch
        {
            QuerySpecification select => [.. select.SelectElements.Select(e => e is SelectScalarExpression scalar
                ? scalar.ColumnName?.Value ?? (scalar.Expression is ColumnReferenceExpression c ? Last(c) : null) : null)],
            BinaryQueryExpression union => Names(union.FirstQueryExpression),
            QueryParenthesisExpression parenthesis => Names(parenthesis.QueryExpression),
            _ => [],
        };
    }

    /// <summary>Each operand a statement converts to text or bytes, wherever it stands: a CAST's, a CONVERT's, a TRY_CAST's and a
    /// TRY_CONVERT's to a character or binary type, each argument of CONCAT and CONCAT_WS, and the first of STR and FORMAT.</summary>
    private sealed class Converted(Action<ScalarExpression?> operand) : TSqlFragmentVisitor
    {
        public override void Visit(CastCall node) => ConvertedTo(node.DataType, node.Parameter);

        public override void Visit(ConvertCall node) => ConvertedTo(node.DataType, node.Parameter);

        public override void Visit(TryCastCall node) => ConvertedTo(node.DataType, node.Parameter);

        public override void Visit(TryConvertCall node) => ConvertedTo(node.DataType, node.Parameter);

        public override void Visit(FunctionCall node)
        {
            var name = node.CallTarget is null ? node.FunctionName.Value : "";
            IEnumerable<ScalarExpression> arguments = Same(name, "CONCAT") || Same(name, "CONCAT_WS") ? node.Parameters
                : Same(name, "STR") || Same(name, "FORMAT") ? node.Parameters.Take(1) : [];
            foreach (var argument in arguments)
            {
                operand(argument);
            }
        }

        private void ConvertedTo(DataTypeReference type, ScalarExpression converted)
        {
            if (type is SqlDataTypeReference { SqlDataTypeOption: SqlDataTypeOption.Char or SqlDataTypeOption.VarChar
                    or SqlDataTypeOption.NChar or SqlDataTypeOption.NVarChar or SqlDataTypeOption.Text or SqlDataTypeOption.NText
                    or SqlDataTypeOption.Binary or SqlDataTypeOption.VarBinary or SqlDataTypeOption.Image }
                || (type is UserDataTypeReference { Name: { SchemaIdentifier: null, BaseIdentifier.Value: var alias } }
                    && CharacterOrBinary.Contains(alias)))
            {
                operand(converted);
            }
        }
    }

    /// <summary>The name of the table a FROM names under an alias, its joins' included; null where none has it.</summary>
    private static SchemaObjectName? Aliased(IEnumerable<TableReference> joined, string alias) => joined.Select(r => r switch
    {
        NamedTableReference named when Same(named.Alias?.Value, alias) => named.SchemaObject,
        JoinTableReference join => Aliased([join.FirstTableReference, join.SecondTableReference], alias),
        JoinParenthesisTableReference parenthesis => Aliased([parenthesis.Join], alias),
        _ => null,
    }).FirstOrDefault(n => n is not null);

    /// <summary>A column a statement names, by its last part.</summary>
    private static string? Last(ColumnReferenceExpression column) => column.MultiPartIdentifier?.Identifiers is { Count: > 0 } parts
        ? parts[^1].Value : null;

    /// <summary>Two names as a case-insensitive database compares them; the copy's own collation is not read, and a case-sensitive one
    /// can only name fewer objects alike.</summary>
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChangeLite;

public sealed record Check(string Schema, string Name, int Line)
{
    public string Table => Names.Quote(Schema, Name);
}

public static class BlockOnPossibleDataLoss
{
    public const string State = "127";

    public static IReadOnlyList<Check> Checks(DeployScript script) => script.Statements
        .Select(s => s.Node is IfStatement i && Matches(i) is { } table ? new Check(table.SchemaIdentifier?.Value ?? "dbo", table.BaseIdentifier.Value, s.Line) : null)
        .OfType<Check>()
        .DistinctBy(c => c.Table)
        .ToList();

    private static SchemaObjectName? Matches(IfStatement statement)
    {
        if (statement.ElseStatement is not null || statement.ThenStatement is not RaiseErrorStatement raise || raise.ThirdParameter is not IntegerLiteral { Value: State })
        {
            return null;
        }

        if (statement.Predicate is not ExistsPredicate { Subquery.QueryExpression: QuerySpecification query } || query.TopRowFilter is null)
        {
            return null;
        }

        if (query.FromClause?.TableReferences is not [NamedTableReference table] || query.WhereClause is not null)
        {
            return null;
        }

        return table.SchemaObject;
    }
}

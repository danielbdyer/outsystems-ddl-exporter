using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChangeLite;

public sealed record Cause(string Name, string Why, string Action);

public static class Causes
{
    public static IReadOnlyList<Cause> Known(string tool) =>
        JsonSerializer.Deserialize<List<Cause>>(File.ReadAllText(Path.Combine(tool, "causes.json")), Answer.Json) ?? [];

    public static string? Of(PlanOperation operation, IReadOnlyList<PlanOperation> all, Model change, string script)
    {
        if (operation.Type == "SqlCheckConstraint" && change.CheckConstraints.Any(c => c.Name.Equals(operation.Name, StringComparison.OrdinalIgnoreCase) && WrittenWithIn(c.Expression)))
        {
            return "check-written-with-in";
        }

        if (operation.Kind is "Drop" or "Create" && all.Any(o => o != operation && o.Type == operation.Type && o.Kind != operation.Kind
            && o.Name.Equals(operation.Name, StringComparison.OrdinalIgnoreCase) && !o.Name.Equals(operation.Name, StringComparison.Ordinal)))
        {
            return "case-only-rename";
        }

        if (operation.Type == "SqlDatabaseOptions" && script.Contains("QUERY_STORE", StringComparison.OrdinalIgnoreCase))
        {
            return "query-store-option";
        }

        return null;
    }

    public static bool WrittenWithIn(string expression)
    {
        var parser = new TSql160Parser(true);
        var parsed = parser.ParseBooleanExpression(new StringReader(expression), out var errors);
        if (errors.Count > 0 || parsed is null)
        {
            return false;
        }

        var visitor = new InVisitor();
        parsed.Accept(visitor);
        return visitor.Found;
    }

    private sealed class InVisitor : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(InPredicate node) => Found = true;
    }
}

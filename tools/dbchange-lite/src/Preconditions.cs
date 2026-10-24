using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChangeLite;

public sealed record Precondition(string Kind, string Schema, string Table, IReadOnlyList<string> Columns, string? Inner, bool OnNewObject, int Line, bool ReadsNew = false)
{
    public static Precondition Of(string kind, string schema, string table, IEnumerable<string> columns, string? inner, bool onNewObject, int line) =>
        new(kind, schema, table, columns.Order(StringComparer.Ordinal).ToList(), inner, onNewObject, line);

    public string Quoted => Names.Quote(Schema, Table);

    public string Spelled => Kind + " on " + Quoted + (Columns.Count > 0 ? " (" + string.Join(", ", Columns.Select(Names.Quote)) + ")" : "");

    public string Query => Inner is null ? "SELECT CASE WHEN EXISTS (SELECT TOP 1 1 FROM " + Quoted + ") THEN 1 ELSE 0 END" : "SELECT CASE WHEN EXISTS (" + Inner + ") THEN 1 ELSE 0 END";

    public string CountQuery => Inner is null ? "SELECT COUNT_BIG(*) FROM " + Quoted : "SELECT COUNT_BIG(*) FROM (" + Inner + ") AS violating";
}

public static class Preconditions
{
    public static IReadOnlyList<Precondition> Of(DeployScript script, IReadOnlyList<Check> checks, Model environment)
    {
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nullNew = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in script.Statements)
        {
            switch (statement.Node)
            {
                case CreateTableStatement create when !NotPredictable.IsRebuildCopy(create.SchemaObjectName):
                    created.Add(DeployScripts.TableOf(create.SchemaObjectName));
                    break;
                case AlterTableAddTableElementStatement add:
                    foreach (var column in add.Definition.ColumnDefinitions)
                    {
                        newColumns.Add(DeployScripts.TableOf(add.SchemaObjectName) + "." + column.ColumnIdentifier.Value);
                        if (column.DefaultConstraint is null && !column.Constraints.OfType<NullableConstraintDefinition>().Any(n => !n.Nullable)
                            && !add.Definition.TableConstraints.OfType<DefaultConstraintDefinition>().Any(d => d.Column?.Value.Equals(column.ColumnIdentifier.Value, StringComparison.OrdinalIgnoreCase) == true))
                        {
                            nullNew.Add(DeployScripts.TableOf(add.SchemaObjectName) + "." + column.ColumnIdentifier.Value);
                        }
                    }

                    break;
            }
        }

        bool IsNew(string table, IEnumerable<string> columns) => created.Contains(table) || columns.Any(c => newColumns.Contains(table + "." + c));
        var found = checks.Select(c => Precondition.Of("TableEmpty", c.Schema, c.Name, [], null, false, c.Line)).ToList();
        foreach (var statement in script.Statements)
        {
            switch (statement.Node)
            {
                case CreateTableStatement create when NotPredictable.IsTemp(create.SchemaObjectName) || NotPredictable.IsHousekeeping(DeployScripts.TableOf(create.SchemaObjectName)) && !NotPredictable.IsRebuildCopy(create.SchemaObjectName):
                    break;
                case CreateTableStatement create when NotPredictable.IsRebuildCopy(create.SchemaObjectName):
                    var (rebuiltSchema, rebuilt) = Original(create.SchemaObjectName);
                    found.AddRange(Constraints(rebuiltSchema, rebuilt, create.Definition, statement.Line, _ => false));
                    found.AddRange(create.Definition.ColumnDefinitions
                        .Where(c => c.Constraints.OfType<NullableConstraintDefinition>().Any(n => !n.Nullable) && environment.ColumnNullable(rebuiltSchema, rebuilt, c.ColumnIdentifier.Value) == true)
                        .Select(c => Precondition.Of("NotNull", rebuiltSchema, rebuilt, [c.ColumnIdentifier.Value],
                            "SELECT 1 AS present FROM " + Names.Quote(rebuiltSchema, rebuilt) + " WHERE " + Names.Quote(c.ColumnIdentifier.Value) + " IS NULL", false, statement.Line)));
                    break;
                case CreateTableStatement create:
                    var (createdSchema, createdTable) = Parts(create.SchemaObjectName);
                    found.AddRange(Constraints(createdSchema, createdTable, create.Definition, statement.Line, _ => true));
                    break;
                case AlterTableAddTableElementStatement add:
                    var (addSchema, addTable) = Parts(add.SchemaObjectName);
                    found.AddRange(Constraints(addSchema, addTable, add.Definition, statement.Line, columns => IsNew(DeployScripts.TableOf(add.SchemaObjectName), columns)));
                    break;
                case CreateIndexStatement { Unique: true } index:
                    var columns = index.Columns.Select(ColumnName).ToList();
                    var filter = index.FilterPredicate is null ? "" : " WHERE " + DeployScripts.Render(index.FilterPredicate);
                    var (indexSchema, indexTable) = Parts(index.OnName);
                    found.Add(Unique(indexSchema, indexTable, columns, filter, statement.Line, IsNew(DeployScripts.TableOf(index.OnName), columns)));
                    break;
                case AlterTableAlterColumnStatement { AlterTableAlterColumnOption: AlterTableAlterColumnOption.NotNull } alter:
                    var (schema, table) = Parts(alter.SchemaObjectName);
                    var name = alter.ColumnIdentifier.Value;
                    if (environment.ColumnNullable(schema, table, name) == true)
                    {
                        found.Add(Precondition.Of("NotNull", schema, table, [name], "SELECT 1 AS present FROM " + Names.Quote(schema, table) + " WHERE " + Names.Quote(name) + " IS NULL", false, statement.Line));
                    }

                    break;
            }
        }

        return found.Select(p => p.OnNewObject && !created.Contains(p.Quoted) && !(p.Kind == "ForeignKey" && p.Columns.Any(c => nullNew.Contains(p.Quoted + "." + c))) ? p with { ReadsNew = true } : p).DistinctBy(p => p.Spelled).OrderBy(p => p.Kind == "TableEmpty" ? 0 : 1).ThenBy(p => p.Line).ThenBy(p => p.Spelled, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<Precondition> Constraints(string schema, string table, TableDefinition definition, int line, Func<IReadOnlyList<string>, bool> isNew)
    {
        var quoted = Names.Quote(schema, table);
        var constraints = definition.TableConstraints.Select(c => (Constraint: c, Column: (string?)null))
            .Concat(definition.ColumnDefinitions.SelectMany(c => c.Constraints.Select(k => (Constraint: k, Column: (string?)c.ColumnIdentifier.Value))));
        foreach (var (constraint, column) in constraints)
        {
            switch (constraint)
            {
                case ForeignKeyConstraintDefinition fk:
                    var keys = fk.Columns.Count > 0 ? fk.Columns.Select(c => c.Value).ToList() : [column!];
                    var parents = fk.ReferencedTableColumns.Select(c => c.Value).ToList();
                    var (parentSchema, parentTable) = Parts(fk.ReferenceTableName);
                    var matches = string.Join(" AND ", keys.Select((k, i) => "parent." + Names.Quote(parents.Count > i ? parents[i] : k) + " = child." + Names.Quote(k)));
                    var inner = "SELECT 1 AS present FROM " + quoted + " AS child WHERE " + string.Join(" AND ", keys.Select(k => "child." + Names.Quote(k) + " IS NOT NULL"))
                        + " AND NOT EXISTS (SELECT 1 FROM " + Names.Quote(parentSchema, parentTable) + " AS parent WHERE " + matches + ")";
                    yield return Precondition.Of("ForeignKey", schema, table, keys, inner, isNew(keys), line);
                    break;
                case CheckConstraintDefinition check:
                    var referenced = Columns(check.CheckCondition);
                    yield return Precondition.Of("Check", schema, table, referenced, "SELECT 1 AS present FROM " + quoted + " WHERE NOT (" + DeployScripts.Render(check.CheckCondition) + ")", isNew(referenced), line);
                    break;
                case UniqueConstraintDefinition unique:
                    var columns = unique.Columns.Count > 0 ? unique.Columns.Select(ColumnName).ToList() : [column!];
                    yield return Unique(schema, table, columns, "", line, isNew(columns));
                    break;
            }
        }
    }

    private static Precondition Unique(string schema, string table, IReadOnlyList<string> columns, string filter, int line, bool onNew)
    {
        var inner = "SELECT 1 AS present FROM " + Names.Quote(schema, table) + filter + " GROUP BY " + string.Join(", ", columns.Select(Names.Quote)) + " HAVING COUNT(*) > 1";
        return Precondition.Of("Unique", schema, table, columns, inner, onNew, line);
    }

    public static (string Schema, string Table) Parts(SchemaObjectName name) => (name.SchemaIdentifier?.Value ?? "dbo", name.BaseIdentifier.Value);


    public static (string Schema, string Table) Original(SchemaObjectName name) => (name.SchemaIdentifier?.Value ?? "dbo", name.BaseIdentifier.Value[NotPredictable.RebuildPrefix.Length..]);

    public static string ColumnName(ColumnWithSortOrder column) => column.Column.MultiPartIdentifier.Identifiers[^1].Value;

    private static IReadOnlyList<string> Columns(BooleanExpression expression)
    {
        var visitor = new ColumnVisitor();
        expression.Accept(visitor);
        return visitor.Names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private sealed class ColumnVisitor : TSqlFragmentVisitor
    {
        public List<string> Names { get; } = [];

        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.MultiPartIdentifier is { Identifiers.Count: > 0 })
            {
                Names.Add(node.MultiPartIdentifier.Identifiers[^1].Value);
            }
        }
    }
}

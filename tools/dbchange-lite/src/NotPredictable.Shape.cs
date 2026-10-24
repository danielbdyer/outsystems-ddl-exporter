using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChangeLite;

public static partial class NotPredictable
{
    public static PlanShape Shape(DeployScript script)
    {
        var altered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rebuilt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var constraints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in script.Statements)
        {
            switch (statement.Node)
            {
                case AlterTableConstraintModificationStatement:
                    break;
                case AlterTableStatement alter when !IsRebuildCopy(alter.SchemaObjectName) && !IsTemp(alter.SchemaObjectName):
                    altered.Add(DeployScripts.TableOf(alter.SchemaObjectName));
                    if (alter is AlterTableAddTableElementStatement add)
                    {
                        constraints.UnionWith(Named(add.Definition));
                    }

                    break;
                case DropTableStatement drop:
                    dropped.UnionWith(drop.Objects.Where(o => !IsRebuildCopy(o) && !IsTemp(o)).Select(DeployScripts.TableOf));
                    break;
                case CreateTableStatement create when !IsRebuildCopy(create.SchemaObjectName) && !IsTemp(create.SchemaObjectName):
                    created.Add(DeployScripts.TableOf(create.SchemaObjectName));
                    constraints.UnionWith(Named(create.Definition));
                    break;
                case InsertStatement insert when RebuiltBy(insert) is { } table:
                    rebuilt.Add(table);
                    break;
                case ExecuteStatement execute when RenamedBy(execute) is { } renamed:
                    altered.Add(renamed);
                    break;
            }
        }

        dropped.ExceptWith(rebuilt);
        return new PlanShape(altered, dropped, rebuilt, created, constraints);
    }

    public static IReadOnlyList<ContractChange> Contract(DeployScript script, PlanShape shape)
    {
        var changes = new List<ContractChange>();
        foreach (var statement in script.Statements)
        {
            switch (statement.Node)
            {
                case DropTableStatement drop:
                    changes.AddRange(drop.Objects.Where(o => !IsRebuildCopy(o) && !IsTemp(o)).Select(DeployScripts.TableOf).Where(t => !shape.Rebuilt.Contains(t)).Select(t => new ContractChange(t, "the table is dropped", statement.Line)));
                    break;
                case AlterTableDropTableElementStatement drop:
                    changes.AddRange(drop.AlterTableDropTableElements.Where(e => e.TableElementType == TableElementType.Column)
                        .Select(e => new ContractChange(DeployScripts.TableOf(drop.SchemaObjectName), "the column " + Names.Quote(e.Name.Value) + " is dropped", statement.Line)));
                    break;
                case AlterTableAlterColumnStatement alter when alter.DataType is not null || alter.AlterTableAlterColumnOption == AlterTableAlterColumnOption.NotNull:
                    changes.Add(new ContractChange(DeployScripts.TableOf(alter.SchemaObjectName), "the column " + Names.Quote(alter.ColumnIdentifier.Value)
                        + (alter.AlterTableAlterColumnOption == AlterTableAlterColumnOption.NotNull ? " becomes mandatory" : " changes type"), statement.Line));
                    break;
                case ExecuteStatement execute when RenamedBy(execute) is { } renamed && !IsRebuildCopyName(RenamedObject(execute)):
                    changes.Add(new ContractChange(renamed, RenamedObject(execute) + " is renamed", statement.Line));
                    break;
            }
        }

        return changes;
    }

    public static IReadOnlyList<(string Table, string Column, int Line)> SmartDefaults(DeployScript script, Model change) => script.Statements
        .Select(s => (Statement: s, Add: s.Node as AlterTableAddTableElementStatement))
        .Where(s => s.Add is not null)
        .SelectMany(s => s.Add!.Definition.ColumnDefinitions
            .Where(c => c.DefaultConstraint is not null && c.Constraints.OfType<NullableConstraintDefinition>().Any(n => !n.Nullable)
                && !change.Defaulted.Contains(DeployScripts.TableOf(s.Add.SchemaObjectName) + "." + Names.Quote(c.ColumnIdentifier.Value)))
            .Select(c => (DeployScripts.TableOf(s.Add.SchemaObjectName), c.ColumnIdentifier.Value, s.Statement.Line)))
        .ToList();

    public static IReadOnlyList<PlanOperation> Unexplained(IReadOnlyList<PlanOperation> operations, string intent, string tool)
    {
        var path = Path.Combine(tool, "operations.json");
        var known = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path)) ?? [];
        if (!known.TryGetValue(intent, out var pairs))
        {
            throw Stop.Of("bad-arguments", "intent.unknown", intent, "operations.json names no operation " + intent + ".", "Name one of " + string.Join(", ", known.Keys.Order(StringComparer.Ordinal)) + ".");
        }

        var allowed = new HashSet<string>(pairs, StringComparer.Ordinal);
        return operations.Where(o => !allowed.Contains(o.Pair) && !o.Name.Contains(RebuildPrefix, StringComparison.OrdinalIgnoreCase) && !o.Name.Contains("__RefactorLog", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static string? RenamedBy(ExecuteStatement execute)
    {
        var renamed = RenamedObject(execute);
        if (renamed is null)
        {
            return null;
        }

        var parts = renamed.Split("].[");
        return parts.Length >= 2 ? Names.Quote(parts[0].TrimStart('['), parts[1].TrimEnd(']')) : null;
    }

    private static string? RenamedObject(ExecuteStatement execute)
    {
        if (execute.ExecuteSpecification.ExecutableEntity is not ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value: var name } procedure
            || !name.Equals("sp_rename", StringComparison.OrdinalIgnoreCase) || procedure.Parameters.Count == 0 || procedure.Parameters[0].ParameterValue is not StringLiteral first)
        {
            return null;
        }

        var parts = first.Value.Split('.').Select(p => p.Trim('[', ']')).ToList();
        return parts.Count switch
        {
            1 => Names.Quote("dbo", parts[0]),
            _ => Names.Quote(parts),
        };
    }

    private static bool IsRebuildCopy(SchemaObjectName name) => name.BaseIdentifier.Value.StartsWith(RebuildPrefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsTemp(SchemaObjectName name) => name.BaseIdentifier.Value.StartsWith('#');

    private static bool IsRebuildCopyName(string? quoted) => quoted is not null && quoted.Contains("[" + RebuildPrefix, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Named(TableDefinition definition) =>
        definition.TableConstraints.Select(c => c.ConstraintIdentifier?.Value).Concat(definition.ColumnDefinitions.SelectMany(c => c.Constraints.Select(k => k.ConstraintIdentifier?.Value))).OfType<string>();
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChangeLite;

public sealed record Unsettled(string Why, Statement Statement, string? Table);

public sealed record ContractChange(string Table, string What, int Line);

public sealed record PlanShape(IReadOnlySet<string> Altered, IReadOnlySet<string> Dropped, IReadOnlySet<string> Rebuilt, IReadOnlySet<string> Created, IReadOnlySet<string> AddedConstraints)
{
    public bool Touches(string table) => Altered.Contains(table) || Dropped.Contains(table) || Rebuilt.Contains(table);
}

public static partial class NotPredictable
{
    public const string RebuildPrefix = "tmp_ms_xx_";

    private static readonly HashSet<string> MovesNoRows = new(StringComparer.Ordinal)
    {
        "CreateTableStatement", "CreateIndexStatement", "AlterTableAddTableElementStatement", "AlterTableDropTableElementStatement", "AlterTableAlterColumnStatement",
        "AlterTableSetStatement", "AlterTableRebuildStatement", "AlterTableTriggerModificationStatement", "AlterTableChangeTrackingModificationStatement", "AlterTableAlterIndexStatement",
        "DropTableStatement", "DropIndexStatement", "CreateViewStatement", "AlterViewStatement", "DropViewStatement", "CreateOrAlterViewStatement", "CreateProcedureStatement",
        "AlterProcedureStatement", "DropProcedureStatement", "CreateOrAlterProcedureStatement", "CreateFunctionStatement", "AlterFunctionStatement", "DropFunctionStatement",
        "CreateOrAlterFunctionStatement", "CreateTriggerStatement", "AlterTriggerStatement", "DropTriggerStatement", "CreateOrAlterTriggerStatement", "EnableDisableTriggerStatement",
        "CreateSchemaStatement", "DropSchemaStatement", "CreateSequenceStatement", "AlterSequenceStatement", "DropSequenceStatement", "CreateSynonymStatement", "DropSynonymStatement",
        "CreateTypeUddtStatement", "CreateTypeTableStatement", "DropTypeStatement", "CreateRoleStatement", "AlterRoleStatement", "DropRoleStatement", "CreateUserStatement",
        "AlterUserStatement", "DropUserStatement", "GrantStatement", "RevokeStatement", "DenyStatement", "CreateDefaultStatement", "DropDefaultStatement", "CreateStatisticsStatement",
        "DropStatisticsStatement", "CreateXmlSchemaCollectionStatement", "AlterXmlSchemaCollectionStatement", "DropXmlSchemaCollectionStatement", "AlterDatabaseSetStatement",
        "PrintStatement", "PredicateSetStatement", "SetCommandStatement", "SetTransactionIsolationLevelStatement", "SetVariableStatement", "DeclareVariableStatement",
        "SetIdentityInsertStatement", "SetRowCountStatement", "SetTextSizeStatement", "SetStatisticsStatement", "SetErrorLevelStatement", "SetOffsetsStatement",
        "DeclareCursorStatement", "OpenCursorStatement", "FetchCursorStatement", "CloseCursorStatement", "DeallocateCursorStatement", "WhileStatement", "IfStatement",
        "BeginEndBlockStatement", "TryCatchStatement", "BeginTransactionStatement", "CommitTransactionStatement", "RollbackTransactionStatement", "SaveTransactionStatement",
        "UseStatement", "RaiseErrorStatement", "ThrowStatement", "ReturnStatement", "GoToStatement", "LabelStatement", "BreakStatement", "ContinueStatement",
    };

    private static readonly HashSet<string> Procedures = new(StringComparer.OrdinalIgnoreCase)
    {
        "sp_rename", "sp_refreshsqlmodule", "sp_refreshview", "sp_addextendedproperty", "sp_updateextendedproperty", "sp_dropextendedproperty",
    };

    public static IReadOnlyList<Unsettled> Of(DeployScript script, PlanShape shape, Model environment, IReadOnlyCollection<string> cdcTracked)
    {
        var unsettled = new List<Unsettled>();
        foreach (var statement in script.Statements)
        {
            switch (statement.Node)
            {
                case InsertStatement insert when RebuiltBy(insert) is { } table:
                    unsettled.Add(new Unsettled("rebuild", statement, table));
                    if (NewIdentity(script, table) is { } identity && environment.Tables.TryGetValue(table, out var shape0) && shape0.HasIdentity != identity)
                    {
                        unsettled.Add(new Unsettled("identity", statement, table));
                    }

                    break;
                case InsertStatement or UpdateStatement or DeleteStatement or MergeStatement or TruncateTableStatement or BulkInsertStatement or SelectStatement { Into: not null }:
                    if (RowMoving(statement, shape) is { } why)
                    {
                        unsettled.Add(new Unsettled(why, statement, Target(statement.Node)));
                    }

                    break;
                case ExecuteStatement execute:
                    if (!Allowed(execute, statement.Batch))
                    {
                        unsettled.Add(new Unsettled("unrecognised", statement, null));
                    }

                    break;
                case AlterTableConstraintModificationStatement { ConstraintEnforcement: ConstraintEnforcement.Check, ExistingRowsCheckEnforcement: ConstraintEnforcement.Check } trust:
                    if (trust.All || trust.ConstraintNames.Any(n => !shape.AddedConstraints.Contains(n.Value)))
                    {
                        unsettled.Add(new Unsettled("re-trust", statement, DeployScripts.TableOf(trust.SchemaObjectName)));
                    }

                    break;
                case SelectStatement:
                    break;
                default:
                    if (!MovesNoRows.Contains(statement.Kind))
                    {
                        unsettled.Add(new Unsettled("unrecognised", statement, null));
                    }

                    break;
            }
        }

        foreach (var table in cdcTracked.Where(shape.Touches))
        {
            var first = script.Statements.FirstOrDefault(s => Target(s.Node) is { } t && t.Equals(table, StringComparison.OrdinalIgnoreCase)) ?? script.Statements[0];
            unsettled.Add(new Unsettled("cdc", first, table));
        }

        return unsettled.DistinctBy(u => (u.Why, u.Statement.Line, u.Table)).ToList();
    }

    private static string? RowMoving(Statement statement, PlanShape shape)
    {
        var target = Target(statement.Node);
        if (target is null || target.StartsWith("[#", StringComparison.Ordinal) || target.Contains("__RefactorLog", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (statement.Part == "own")
        {
            return "unrecognised";
        }

        return shape.Touches(target) ? "authored" : null;
    }

    private static string? Target(TSqlStatement node) => node switch
    {
        InsertStatement i => Temp(i.InsertSpecification.Target) ?? DeployScripts.TargetOf(i.InsertSpecification.Target),
        UpdateStatement u => Temp(u.UpdateSpecification.Target) ?? Aliased(u.UpdateSpecification.Target, u.UpdateSpecification.FromClause) ?? DeployScripts.TargetOf(u.UpdateSpecification.Target),
        DeleteStatement d => Temp(d.DeleteSpecification.Target) ?? Aliased(d.DeleteSpecification.Target, d.DeleteSpecification.FromClause) ?? DeployScripts.TargetOf(d.DeleteSpecification.Target),
        MergeStatement m => Temp(m.MergeSpecification.Target) ?? DeployScripts.TargetOf(m.MergeSpecification.Target),
        TruncateTableStatement t => DeployScripts.TableOf(t.TableName),
        SelectStatement { Into: { } into } => DeployScripts.TableOf(into),
        AlterTableStatement a => DeployScripts.TableOf(a.SchemaObjectName),
        DropTableStatement { Objects: [var first, ..] } => DeployScripts.TableOf(first),
        _ => null,
    };

    private static string? Aliased(TableReference? target, FromClause? from) => target is NamedTableReference { SchemaObject: { SchemaIdentifier: null, BaseIdentifier.Value: var alias } }
        && from?.TableReferences.SelectMany(NamedIn).FirstOrDefault(n => n.Alias?.Value.Equals(alias, StringComparison.OrdinalIgnoreCase) == true) is { } table ? Temp(table) ?? DeployScripts.TableOf(table.SchemaObject) : null;

    private static IEnumerable<NamedTableReference> NamedIn(TableReference reference) => reference switch
    {
        NamedTableReference named => [named],
        JoinTableReference join => NamedIn(join.FirstTableReference).Concat(NamedIn(join.SecondTableReference)),
        _ => [],
    };

    private static string? Temp(TableReference? target) => target is NamedTableReference { SchemaObject.BaseIdentifier.Value: var name } && name.StartsWith('#') ? "[" + name + "]" : null;

    private static string? RebuiltBy(InsertStatement insert)
    {
        if (insert.InsertSpecification.Target is not NamedTableReference { SchemaObject: var target } || !IsRebuildCopy(target) || insert.InsertSpecification.InsertSource is not SelectInsertSource { Select: QuerySpecification select })
        {
            return null;
        }

        return select.FromClause?.TableReferences is [NamedTableReference source] ? DeployScripts.TableOf(source.SchemaObject) : null;
    }

    private static bool? NewIdentity(DeployScript script, string table)
    {
        var copy = script.Statements.Select(s => s.Node).OfType<CreateTableStatement>()
            .FirstOrDefault(c => IsRebuildCopy(c.SchemaObjectName) && DeployScripts.TableOf(c.SchemaObjectName).Equals(Names.Quote(c.SchemaObjectName.SchemaIdentifier?.Value ?? "dbo", RebuildPrefix + table.Split("].[")[1].TrimEnd(']')), StringComparison.OrdinalIgnoreCase));
        return copy is null ? null : copy.Definition.ColumnDefinitions.Any(c => c.IdentityOptions is not null);
    }

    private static bool Allowed(ExecuteStatement execute, TSqlBatch batch)
    {
        if (execute.ExecuteSpecification.ExecutableEntity is not ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value: var name } procedure)
        {
            return false;
        }

        if (Procedures.Contains(name))
        {
            return true;
        }

        if (!name.Equals("sp_executesql", StringComparison.OrdinalIgnoreCase) || procedure.Parameters.Count == 0)
        {
            return false;
        }

        var text = procedure.Parameters[0].ParameterValue switch
        {
            StringLiteral literal => literal.Value,
            VariableReference variable => string.Concat(batch.Statements.SelectMany(s => DeployScripts.Flatten([s], 0)).Select(f => f.Node).OfType<SetVariableStatement>()
                .Where(s => s.Variable.Name.Equals(variable.Name, StringComparison.OrdinalIgnoreCase)).SelectMany(s => Literals(s.Expression))),
            _ => "",
        };
        return text.StartsWith("ALTER TABLE [", StringComparison.OrdinalIgnoreCase) && text.Contains(" CHECK CONSTRAINT [", StringComparison.OrdinalIgnoreCase) && !text.Contains(';', StringComparison.Ordinal);
    }

    private static IEnumerable<string> Literals(ScalarExpression expression) => expression switch
    {
        StringLiteral s => [s.Value],
        BinaryExpression b => Literals(b.FirstExpression).Concat(Literals(b.SecondExpression)),
        ParenthesisExpression p => Literals(p.Expression),
        SimpleCaseExpression c => c.WhenClauses.Select(w => w.ThenExpression).Append(c.ElseExpression).OfType<ScalarExpression>().SelectMany(Literals),
        SearchedCaseExpression c => c.WhenClauses.Select(w => w.ThenExpression).Append(c.ElseExpression).OfType<ScalarExpression>().SelectMany(Literals),
        _ => [],
    };

}

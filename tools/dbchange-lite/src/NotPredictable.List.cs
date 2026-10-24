using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChangeLite;

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
        "AlterIndexStatement", "AlterSchemaStatement", "AlterAuthorizationStatement", "CreateXmlIndexStatement", "CreateColumnStoreIndexStatement", "CreateFullTextIndexStatement",
        "DropFullTextIndexStatement", "CreatePartitionFunctionStatement", "CreatePartitionSchemeStatement", "DropPartitionFunctionStatement", "DropPartitionSchemeStatement",
    };

    private static readonly HashSet<string> Procedures = new(StringComparer.OrdinalIgnoreCase)
    {
        "sp_rename", "sp_refreshsqlmodule", "sp_refreshview", "sp_addextendedproperty", "sp_updateextendedproperty", "sp_dropextendedproperty",
    };

    public static bool IsHousekeeping(string name) => name.Contains(RebuildPrefix, StringComparison.OrdinalIgnoreCase) || name.Contains("__RefactorLog", StringComparison.OrdinalIgnoreCase);

    public static bool IsRebuildCopy(SchemaObjectName name) => name.BaseIdentifier.Value.StartsWith(RebuildPrefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsTemp(SchemaObjectName name) => name.BaseIdentifier.Value.StartsWith('#');
}

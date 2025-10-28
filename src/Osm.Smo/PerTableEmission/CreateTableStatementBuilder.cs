using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Osm.Smo;
using ScriptDomSortOrder = Microsoft.SqlServer.TransactSql.ScriptDom.SortOrder;

namespace Osm.Smo.PerTableEmission;

internal sealed class CreateTableStatementBuilder
{
    private readonly SqlScriptFormatter _formatter;

    public CreateTableStatementBuilder(SqlScriptFormatter formatter)
    {
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    }

    public CreateTableStatement BuildCreateTableStatement(
        SmoTableDefinition table,
        string effectiveTableName,
        SmoBuildOptions options)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        if (effectiveTableName is null)
        {
            throw new ArgumentNullException(nameof(effectiveTableName));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var definition = new TableDefinition();
        var columnLookup = new Dictionary<string, ColumnDefinition>(table.Columns.Length * 2, StringComparer.OrdinalIgnoreCase);
        var columnNameMap = BuildColumnNameMap(table);
        foreach (var column in table.Columns)
        {
            var columnDefinition = BuildColumnDefinition(column, options, columnNameMap);
            definition.ColumnDefinitions.Add(columnDefinition);
            if (!string.IsNullOrWhiteSpace(column.Name))
            {
                columnLookup[column.Name] = columnDefinition;
            }

            if (!string.IsNullOrWhiteSpace(column.PhysicalName))
            {
                columnLookup[column.PhysicalName] = columnDefinition;
            }
        }

        var primaryKey = table.Indexes.FirstOrDefault(i => i.IsPrimaryKey);
        if (primaryKey is not null)
        {
            var sortedColumns = primaryKey.Columns.OrderBy(c => c.Ordinal).ToImmutableArray();
            var constraintName = _formatter.ResolveConstraintName(primaryKey.Name, table.Name, table.LogicalName, effectiveTableName);

            if (sortedColumns.Length == 1 &&
                columnLookup.TryGetValue(sortedColumns[0].Name, out var primaryKeyColumn))
            {
                var inlineConstraint = new UniqueConstraintDefinition
                {
                    IsPrimaryKey = true,
                    Clustered = true,
                    ConstraintIdentifier = _formatter.CreateIdentifier(constraintName, options.Format),
                };

                primaryKeyColumn.Constraints.Add(inlineConstraint);
            }
            else
            {
                var tableConstraint = new UniqueConstraintDefinition
                {
                    IsPrimaryKey = true,
                    Clustered = true,
                    ConstraintIdentifier = _formatter.CreateIdentifier(constraintName, options.Format),
                };

                foreach (var column in sortedColumns)
                {
                    tableConstraint.Columns.Add(new ColumnWithSortOrder
                    {
                        Column = _formatter.BuildColumnReference(column.Name, options.Format),
                        SortOrder = ScriptDomSortOrder.NotSpecified,
                    });
                }

                definition.TableConstraints.Add(tableConstraint);
            }
        }

        return new CreateTableStatement
        {
            SchemaObjectName = _formatter.BuildSchemaObjectName(table.Schema, effectiveTableName, options.Format),
            Definition = definition,
        };
    }

    public ImmutableArray<string> AddForeignKeys(
        CreateTableStatement statement,
        SmoTableDefinition table,
        string effectiveTableName,
        SmoBuildOptions options,
        out ImmutableDictionary<string, bool> foreignKeyTrustLookup)
    {
        if (statement is null)
        {
            throw new ArgumentNullException(nameof(statement));
        }

        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        if (effectiveTableName is null)
        {
            throw new ArgumentNullException(nameof(effectiveTableName));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.EmitBareTableOnly || table.ForeignKeys.Length == 0 || statement.Definition is null)
        {
            foreignKeyTrustLookup = ImmutableDictionary<string, bool>.Empty;
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(table.ForeignKeys.Length);
        var trustBuilder = ImmutableDictionary.CreateBuilder<string, bool>(StringComparer.OrdinalIgnoreCase);
        var columnLookup = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var columnDefinition in statement.Definition.ColumnDefinitions)
        {
            if (columnDefinition?.ColumnIdentifier?.Value is { Length: > 0 } name)
            {
                columnLookup[name] = columnDefinition;
            }
        }

        foreach (var foreignKey in table.ForeignKeys)
        {
            var referencedTableName = options.NamingOverrides.GetEffectiveTableName(
                foreignKey.ReferencedSchema,
                foreignKey.ReferencedTable,
                foreignKey.ReferencedLogicalTable,
                foreignKey.ReferencedModule);

            var foreignKeyName = _formatter.ResolveConstraintName(foreignKey.Name, table.Name, table.LogicalName, effectiveTableName);
            builder.Add(foreignKeyName);
            trustBuilder[foreignKeyName] = foreignKey.IsNoCheck;

            var deleteAction = MapDeleteAction(foreignKey.DeleteAction);
            var constraint = new ForeignKeyConstraintDefinition
            {
                ConstraintIdentifier = _formatter.CreateIdentifier(foreignKeyName, options.Format),
                ReferenceTableName = _formatter.BuildSchemaObjectName(foreignKey.ReferencedSchema, referencedTableName, options.Format),
            };

            if (deleteAction != DeleteUpdateAction.NoAction)
            {
                constraint.DeleteAction = deleteAction;
            }

            foreach (var column in foreignKey.Columns)
            {
                constraint.Columns.Add(_formatter.CreateIdentifier(column, options.Format));
            }

            foreach (var referencedColumn in foreignKey.ReferencedColumns)
            {
                constraint.ReferencedTableColumns.Add(_formatter.CreateIdentifier(referencedColumn, options.Format));
            }

            if (foreignKey.Columns.Length == 1 &&
                columnLookup.TryGetValue(foreignKey.Columns[0], out var inlineColumn) &&
                inlineColumn is not null)
            {
                inlineColumn.Constraints.Add(constraint);
            }
            else
            {
                statement.Definition.TableConstraints.Add(constraint);
            }
        }

        foreignKeyTrustLookup = trustBuilder.ToImmutable();
        return builder.ToImmutable();
    }

    private ColumnDefinition BuildColumnDefinition(
        SmoColumnDefinition column,
        SmoBuildOptions options,
        IReadOnlyDictionary<string, string> columnNameMap)
    {
        var definition = new ColumnDefinition
        {
            ColumnIdentifier = _formatter.CreateIdentifier(column.Name, options.Format),
            DataType = column.IsComputed ? null : TranslateDataType(column.DataType),
        };

        if (!column.Nullable)
        {
            definition.Constraints.Add(new NullableConstraintDefinition { Nullable = false });
        }

        if (column.IsIdentity && !column.IsComputed)
        {
            definition.IdentityOptions = new IdentityOptions
            {
                IdentitySeed = new IntegerLiteral { Value = column.IdentitySeed.ToString(CultureInfo.InvariantCulture) },
                IdentityIncrement = new IntegerLiteral { Value = column.IdentityIncrement.ToString(CultureInfo.InvariantCulture) },
            };
        }

        if (!string.IsNullOrWhiteSpace(column.Collation) &&
            !string.Equals(column.Collation, "Latin1_General_CI_AI", StringComparison.OrdinalIgnoreCase))
        {
            definition.Collation = _formatter.CreateIdentifier(column.Collation!, options.Format);
        }

        if (!options.EmitBareTableOnly)
        {
            var defaultExpression = ParseExpression(column.DefaultExpression, columnNameMap);
            if (defaultExpression is not null)
            {
                var defaultConstraintDefinition = new DefaultConstraintDefinition
                {
                    Expression = defaultExpression,
                };

                if (column.DefaultConstraint is { Name: { Length: > 0 } name })
                {
                    defaultConstraintDefinition.ConstraintIdentifier = _formatter.CreateIdentifier(name, options.Format);
                }

                definition.Constraints.Add(defaultConstraintDefinition);
            }

            if (!column.CheckConstraints.IsDefaultOrEmpty)
            {
                foreach (var checkConstraint in column.CheckConstraints)
                {
                    var predicate = ParsePredicate(checkConstraint.Expression, columnNameMap);
                    if (predicate is null)
                    {
                        continue;
                    }

                    var checkDefinition = new CheckConstraintDefinition
                    {
                        CheckCondition = predicate,
                    };

                    if (!string.IsNullOrWhiteSpace(checkConstraint.Name))
                    {
                        checkDefinition.ConstraintIdentifier = _formatter.CreateIdentifier(checkConstraint.Name!, options.Format);
                    }

                    definition.Constraints.Add(checkDefinition);
                }
            }
        }

        if (column.IsComputed && !string.IsNullOrWhiteSpace(column.ComputedExpression))
        {
            definition.ComputedColumnExpression = ParseExpression(column.ComputedExpression, columnNameMap);
        }

        return definition;
    }

    private static IReadOnlyDictionary<string, string> BuildColumnNameMap(SmoTableDefinition table)
    {
        if (table.Columns.Length == 0)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.PhysicalName) || string.IsNullOrWhiteSpace(column.Name))
            {
                continue;
            }

            builder[column.PhysicalName] = column.Name;
        }

        return builder.ToImmutable();
    }

    private static ScalarExpression? ParseExpression(
        string? expression,
        IReadOnlyDictionary<string, string> columnNameMap)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var parser = new TSql150Parser(initialQuotedIdentifiers: false);
        using var reader = new StringReader(expression);
        var fragment = parser.ParseExpression(reader, out var errors);
        if (errors is not null && errors.Count > 0)
        {
            return null;
        }

        RewriteColumnReferences(fragment, columnNameMap);
        return fragment;
    }

    private static BooleanExpression? ParsePredicate(
        string? predicate,
        IReadOnlyDictionary<string, string> columnNameMap)
    {
        if (string.IsNullOrWhiteSpace(predicate))
        {
            return null;
        }

        var parser = new TSql150Parser(initialQuotedIdentifiers: false);
        using var reader = new StringReader(predicate);
        var fragment = parser.ParseBooleanExpression(reader, out var errors);
        if (fragment is null || (errors is not null && errors.Count > 0))
        {
            return null;
        }

        RewriteColumnReferences(fragment, columnNameMap);
        return fragment;
    }

    private static void RewriteColumnReferences(
        TSqlFragment? fragment,
        IReadOnlyDictionary<string, string> columnNameMap)
    {
        if (fragment is null || columnNameMap.Count == 0)
        {
            return;
        }

        var visitor = new ColumnReferenceRewriteVisitor(columnNameMap);
        fragment.Accept(visitor);
    }

    private sealed class ColumnReferenceRewriteVisitor : TSqlFragmentVisitor
    {
        private readonly IReadOnlyDictionary<string, string> _columnNameMap;

        public ColumnReferenceRewriteVisitor(IReadOnlyDictionary<string, string> columnNameMap)
        {
            _columnNameMap = columnNameMap;
        }

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            if (node?.MultiPartIdentifier is null || node.MultiPartIdentifier.Identifiers.Count == 0)
            {
                return;
            }

            foreach (var identifier in node.MultiPartIdentifier.Identifiers)
            {
                if (identifier is null || string.IsNullOrEmpty(identifier.Value))
                {
                    continue;
                }

                if (_columnNameMap.TryGetValue(identifier.Value, out var replacement))
                {
                    identifier.Value = replacement;
                }
            }
        }
    }

    private static DataTypeReference TranslateDataType(DataType dataType)
    {
        var sqlType = new SqlDataTypeReference
        {
            SqlDataTypeOption = MapSqlDataType(dataType.SqlDataType),
        };

        if (sqlType.SqlDataTypeOption is SqlDataTypeOption.VarChar or SqlDataTypeOption.NVarChar or SqlDataTypeOption.Char or SqlDataTypeOption.NChar)
        {
            if (dataType.MaximumLength < 0)
            {
                sqlType.Parameters.Add(new MaxLiteral());
            }
            else if (dataType.MaximumLength > 0)
            {
                sqlType.Parameters.Add(new IntegerLiteral { Value = dataType.MaximumLength.ToString(CultureInfo.InvariantCulture) });
            }
        }

        if (sqlType.SqlDataTypeOption is SqlDataTypeOption.Decimal or SqlDataTypeOption.Numeric)
        {
            sqlType.Parameters.Add(new IntegerLiteral { Value = dataType.GetDeclaredPrecision().ToString(CultureInfo.InvariantCulture) });
            sqlType.Parameters.Add(new IntegerLiteral { Value = dataType.GetDeclaredScale().ToString(CultureInfo.InvariantCulture) });
        }

        return sqlType;
    }

    private static SqlDataTypeOption MapSqlDataType(SqlDataType sqlDataType) => sqlDataType switch
    {
        SqlDataType.BigInt => SqlDataTypeOption.BigInt,
        SqlDataType.Bit => SqlDataTypeOption.Bit,
        SqlDataType.Char => SqlDataTypeOption.Char,
        SqlDataType.Date => SqlDataTypeOption.Date,
        SqlDataType.DateTime => SqlDataTypeOption.DateTime,
        SqlDataType.Decimal => SqlDataTypeOption.Decimal,
        SqlDataType.Int => SqlDataTypeOption.Int,
        SqlDataType.NChar => SqlDataTypeOption.NChar,
        SqlDataType.NVarChar => SqlDataTypeOption.NVarChar,
        SqlDataType.NVarCharMax => SqlDataTypeOption.NVarChar,
        SqlDataType.VarChar => SqlDataTypeOption.VarChar,
        SqlDataType.VarCharMax => SqlDataTypeOption.VarChar,
        SqlDataType.VarBinary => SqlDataTypeOption.VarBinary,
        SqlDataType.VarBinaryMax => SqlDataTypeOption.VarBinary,
        SqlDataType.Binary => SqlDataTypeOption.Binary,
        SqlDataType.SmallInt => SqlDataTypeOption.SmallInt,
        SqlDataType.TinyInt => SqlDataTypeOption.TinyInt,
        SqlDataType.UniqueIdentifier => SqlDataTypeOption.UniqueIdentifier,
        SqlDataType.SmallDateTime => SqlDataTypeOption.SmallDateTime,
        SqlDataType.DateTime2 => SqlDataTypeOption.DateTime2,
        SqlDataType.DateTimeOffset => SqlDataTypeOption.DateTimeOffset,
        SqlDataType.Time => SqlDataTypeOption.Time,
        SqlDataType.Money => SqlDataTypeOption.Money,
        SqlDataType.SmallMoney => SqlDataTypeOption.SmallMoney,
        SqlDataType.Real => SqlDataTypeOption.Real,
        SqlDataType.Float => SqlDataTypeOption.Float,
        _ => SqlDataTypeOption.NVarChar,
    };

    private static DeleteUpdateAction MapDeleteAction(ForeignKeyAction action) => action switch
    {
        ForeignKeyAction.Cascade => DeleteUpdateAction.Cascade,
        ForeignKeyAction.SetNull => DeleteUpdateAction.SetNull,
        _ => DeleteUpdateAction.NoAction,
    };
}

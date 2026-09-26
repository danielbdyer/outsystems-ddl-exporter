using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Osm.Emission.Seeds;
using Osm.Emission.Formatting;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using EntityDependencyOrderingResult = Osm.Emission.Seeds.EntityDependencySorter.EntityDependencyOrderingResult;

namespace Osm.Emission;

/// <summary>
/// Generates dynamic entity MERGE statements using phased loading strategy
/// to resolve circular dependencies without disabling constraints.
/// 
/// Strategy:
/// 1. Analyze nullable vs mandatory FK relationships
/// 2. Sort tables by mandatory-edge-only topological order
/// 3. INSERT with nullable FKs set to NULL
/// 4. UPDATE to populate nullable FKs after all INSERTs complete
/// </summary>
public sealed class PhasedDynamicEntityInsertGenerator
{
    private readonly SqlLiteralFormatter _literalFormatter;

    public PhasedDynamicEntityInsertGenerator(SqlLiteralFormatter literalFormatter)
    {
        _literalFormatter = literalFormatter ?? throw new ArgumentNullException(nameof(literalFormatter));
    }

    /// <summary>
    /// Generates phased MERGE scripts for a dataset with circular dependencies.
    /// </summary>
    public PhasedInsertScript Generate(
        DynamicEntityDataset dataset,
        OsmModel? model,
        NamingOverrideOptions? namingOverrides = null,
        EntityDependencySortOptions? sortOptions = null,
        CircularDependencyOptions? circularDependencyOptions = null)
    {
        if (dataset is null || dataset.IsEmpty)
        {
            return PhasedInsertScript.Empty;
        }

        namingOverrides ??= NamingOverrideOptions.Empty;
        sortOptions ??= EntityDependencySortOptions.Default;
        circularDependencyOptions ??= CircularDependencyOptions.Empty;

        // Sort tables using standard dependency sorter
        var ordering = EntityDependencySorter.SortByForeignKeys(
            dataset.Tables,
            model,
            namingOverrides,
            sortOptions,
            circularDependencyOptions);

        if (!ordering.CycleDetected)
        {
            return GenerateStandardMerges(ordering.Tables, model);
        }

        var cycleTableSet = BuildCycleTableSet(ordering);
        return GeneratePhasedMerges(ordering.Tables, model, namingOverrides, cycleTableSet);
    }

    private PhasedInsertScript GenerateStandardMerges(
        ImmutableArray<StaticEntityTableData> tables,
        OsmModel? model)
    {
        var inserts = new List<string>();

        foreach (var table in tables)
        {
            var keyColumns = GetKeyColumns(table.Definition.Columns);
            var mergeSql = GenerateMergeForTable(table, keyColumns, model, null);
            inserts.Add(mergeSql);
        }

        return new PhasedInsertScript(
            PhaseOneInserts: inserts.ToImmutableArray(),
            PhaseTwoUpdates: ImmutableArray<string>.Empty,
            RequiresPhasing: false);
    }

    private PhasedInsertScript GeneratePhasedMerges(
        ImmutableArray<StaticEntityTableData> tables,
        OsmModel? model,
        NamingOverrideOptions namingOverrides,
        HashSet<string> cycleTables)
    {
        var phaseOneInserts = new List<string>();
        var phaseTwoUpdates = new List<string>();

        // Build entity lookup for FK analysis
        var entityLookup = BuildEntityLookup(model);
        var restrictToExplicitCycleSet = cycleTables.Count > 0;

        foreach (var table in tables)
        {
            var keyColumns = GetKeyColumns(table.Definition.Columns);

            var physicalName = table.Definition.PhysicalName ?? table.Definition.LogicalName;
            var qualifiedName = string.IsNullOrWhiteSpace(table.Definition.Schema)
                ? physicalName
                : $"{table.Definition.Schema}.{physicalName}";
            var tableInCycle = !restrictToExplicitCycleSet ||
                (!string.IsNullOrWhiteSpace(physicalName) && cycleTables.Contains(physicalName)) ||
                (!string.IsNullOrWhiteSpace(qualifiedName) && cycleTables.Contains(qualifiedName));

            var lookupKey = table.Definition.PhysicalName ?? table.Definition.LogicalName;

            if (string.IsNullOrWhiteSpace(lookupKey) || !entityLookup.TryGetValue(lookupKey, out var entity))
            {
                // No metadata - emit standard MERGE
                phaseOneInserts.Add(GenerateMergeForTable(table, keyColumns, model, null));
                continue;
            }

            if (!tableInCycle)
            {
                phaseOneInserts.Add(GenerateMergeForTable(table, keyColumns, model, columnsToNull: null));
                continue;
            }

            // Identify nullable FKs that should be NULLed in phase 1
            var nullableFKColumns = IdentifyNullableFKColumns(entity, cycleTables, restrictToExplicitCycleSet);
            // Phase 1: MERGE with nullable FKs = NULL
            phaseOneInserts.Add(GenerateMergeForTable(table, keyColumns, model, nullableFKColumns));

            // Phase 2: UPDATE nullable FKs
            if (nullableFKColumns.Count > 0)
            {
                var updateSql = GenerateUpdateForNullableFKs(table, keyColumns, nullableFKColumns);
                if (!string.IsNullOrEmpty(updateSql))
                {
                    phaseTwoUpdates.Add(updateSql);
                }
            }
        }

        return new PhasedInsertScript(
            PhaseOneInserts: phaseOneInserts.ToImmutableArray(),
            PhaseTwoUpdates: phaseTwoUpdates.ToImmutableArray(),
            RequiresPhasing: true);
    }

    private HashSet<string> IdentifyNullableFKColumns(
        EntityModel entity,
        HashSet<string> tablesInCycle,
        bool restrictToCycle)
    {
        var nullableFKColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relationship in entity.Relationships)
        {
            // Only care about relationships with DB constraints
            if (!relationship.HasDatabaseConstraint)
            {
                continue;
            }

            // Only care about relationships to tables in the cycle
            var targetPhysicalName = relationship.TargetPhysicalName.Value;
            var targetQualifiedName = string.IsNullOrWhiteSpace(entity.Schema.Value)
                ? targetPhysicalName
                : $"{entity.Schema.Value}.{targetPhysicalName}";

            var targetInCycle = !restrictToCycle ||
                tablesInCycle.Contains(targetPhysicalName) ||
                tablesInCycle.Contains(targetQualifiedName);

            if (!targetInCycle)
            {
                continue;
            }

            // Check if the FK column is nullable
            var fkAttribute = entity.Attributes.FirstOrDefault(a =>
                string.Equals(a.LogicalName.Value, relationship.ViaAttribute.Value, StringComparison.OrdinalIgnoreCase));

            if (fkAttribute != null && !fkAttribute.IsMandatory)
            {
                // This is a nullable FK - should be NULLed in phase 1
                nullableFKColumns.Add(fkAttribute.ColumnName.Value);
            }
        }

        return nullableFKColumns;
    }

    private string GenerateMergeForTable(
        StaticEntityTableData table,
        StaticEntitySeedColumn[] keyColumns,
        OsmModel? model,
        HashSet<string>? columnsToNull)
    {
        _ = model; // Reserved for future metadata-driven merge predicate enhancements

        var sb = new StringBuilder();
        sb.AppendLine($"-- MERGE: {table.Definition.Schema}.{table.Definition.PhysicalName}");

        if (table.Rows.IsDefaultOrEmpty)
        {
            sb.AppendLine("-- (no rows)");
            return sb.ToString();
        }

        var schema = table.Definition.Schema;
        var tableName = table.Definition.PhysicalName;
        var columns = table.Definition.Columns;
        var columnList = string.Join(", ", columns.Select(c => $"[{GetEffectiveColumnName(c)}]"));
        var keyPredicate = string.Join(" AND ", keyColumns.Select(c => $"Target.[{GetEffectiveColumnName(c)}] = Source.[{GetEffectiveColumnName(c)}]"));
        var hasIdentity = columns.Any(column => column.IsIdentity);

        if (hasIdentity)
        {
            sb.AppendLine($"SET IDENTITY_INSERT [{schema}].[{tableName}] ON;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        AppendSourceCtes(sb, table, columnList, columnsToNull);

        var sourceName = columnsToNull is { Count: > 0 }
            ? "PhaseOneSource"
            : "SourceRows";

        sb.AppendLine($"MERGE INTO [{schema}].[{tableName}] AS Target");
        sb.AppendLine($"USING {sourceName} AS Source");
        sb.AppendLine();
        sb.AppendLine($"    ON {keyPredicate}");
        sb.AppendLine("WHEN NOT MATCHED THEN INSERT (");
        sb.AppendLine($"    {columnList}");
        sb.AppendLine(")");
        sb.AppendLine("    VALUES (");
        sb.AppendLine($"    {string.Join(", ", columns.Select(c => $"Source.[{GetEffectiveColumnName(c)}]"))}");
        sb.AppendLine(");");
        sb.AppendLine();
        sb.AppendLine("GO");
        sb.AppendLine();

        if (hasIdentity)
        {
            sb.AppendLine($"SET IDENTITY_INSERT [{schema}].[{tableName}] OFF;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string GenerateUpdateForNullableFKs(
        StaticEntityTableData table,
        StaticEntitySeedColumn[] keyColumns,
        HashSet<string> nullableColumns)
    {
        if (table.Rows.IsDefaultOrEmpty || nullableColumns.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"-- UPDATE nullable FKs: {table.Definition.Schema}.{table.Definition.PhysicalName}");

        var schema = table.Definition.Schema;
        var tableName = table.Definition.PhysicalName;
        var columns = table.Definition.Columns;
        var nullableColumnList = columns
            .Where(column => nullableColumns.Contains(column.ColumnName) ||
                             nullableColumns.Contains(column.EffectiveColumnName))
            .Select(column => GetEffectiveColumnName(column))
            .ToArray();

        if (nullableColumnList.Length == 0)
        {
            return string.Empty;
        }

        var columnList = string.Join(", ", columns.Select(c => $"[{GetEffectiveColumnName(c)}]"));
        var keyPredicate = string.Join(" AND ", keyColumns.Select(c => $"Target.[{GetEffectiveColumnName(c)}] = Source.[{GetEffectiveColumnName(c)}]"));

        sb.Append("WITH SourceRows (");
        sb.Append(columnList);
        sb.AppendLine(") AS");
        sb.AppendLine("(");
        AppendValuesClause(sb, table, "    ");
        sb.AppendLine(")");
        sb.AppendLine();

        sb.AppendLine("UPDATE Target");
        sb.AppendLine($"SET {string.Join(", ", nullableColumnList.Select(column => $"[{column}] = Source.[{column}]")).Trim()}");
        sb.AppendLine($"FROM [{schema}].[{tableName}] AS Target");
        sb.AppendLine("JOIN SourceRows AS Source");
        sb.AppendLine($"    ON {keyPredicate};");
        sb.AppendLine();
        sb.AppendLine("GO");
        sb.AppendLine();

        return sb.ToString();
    }

    private static IReadOnlyDictionary<string, EntityModel> BuildEntityLookup(OsmModel? model)
    {
        if (model is null || model.Modules.IsDefaultOrEmpty)
        {
            return new Dictionary<string, EntityModel>(StringComparer.OrdinalIgnoreCase);
        }

        var lookup = new Dictionary<string, EntityModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                lookup[entity.PhysicalName.Value] = entity;
            }
        }

        return lookup;
    }

    private static HashSet<string> BuildCycleTableSet(EntityDependencyOrderingResult ordering)
    {
        if (ordering.StronglyConnectedComponents is null || ordering.StronglyConnectedComponents.Value.IsDefaultOrEmpty)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in ordering.StronglyConnectedComponents.Value)
        {
            if (component.Length < 2)
            {
                continue;
            }

            foreach (var name in component)
            {
                set.Add(name);

                var separatorIndex = name.IndexOf('.', StringComparison.Ordinal);
                if (separatorIndex > 0 && separatorIndex < name.Length - 1)
                {
                    set.Add(name[(separatorIndex + 1)..]);
                }
            }
        }

        return set;
    }

    private static StaticEntitySeedColumn[] GetKeyColumns(ImmutableArray<StaticEntitySeedColumn> columns)
    {
        var keyColumns = columns.Where(c => c.IsPrimaryKey).ToArray();

        if (keyColumns.Length == 0)
        {
            keyColumns = columns.ToArray();
        }

        return keyColumns;
    }

    private void AppendValuesClause(StringBuilder builder, StaticEntityTableData table, string indent)
    {
        builder.Append(indent);
        builder.AppendLine("VALUES");

        var definition = table.Definition;
        var rows = table.Rows;
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            builder.Append(indent);
            builder.Append("    (");
            for (var j = 0; j < definition.Columns.Length; j++)
            {
                if (j > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(_literalFormatter.FormatValue(row.Values[j]));
            }

            builder.Append(')');
            if (i < rows.Length - 1)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }
    }

    private void AppendSourceCtes(
        StringBuilder builder,
        StaticEntityTableData table,
        string columnList,
        HashSet<string>? columnsToNull)
    {
        builder.Append("WITH SourceRows (");
        builder.Append(columnList);
        builder.AppendLine(") AS");
        builder.AppendLine("(");
        AppendValuesClause(builder, table, "    ");
        builder.AppendLine(")");

        if (columnsToNull is { Count: > 0 })
        {
            builder.AppendLine(", PhaseOneSource AS");
            builder.AppendLine("(");
            builder.AppendLine("    SELECT");

            var columns = table.Definition.Columns;
            for (var i = 0; i < columns.Length; i++)
            {
                var column = columns[i];
                var effectiveName = GetEffectiveColumnName(column);
                var projection = columnsToNull.Contains(column.ColumnName) ||
                                 columnsToNull.Contains(column.EffectiveColumnName)
                    ? $"CASE WHEN 1 = 0 THEN SourceRows.[{effectiveName}] ELSE NULL END AS [{effectiveName}]"
                    : $"SourceRows.[{effectiveName}]";

                builder.Append("        ");
                builder.Append(projection);

                if (i < columns.Length - 1)
                {
                    builder.Append(',');
                }

                builder.AppendLine();
            }

            builder.AppendLine("    FROM SourceRows");
            builder.AppendLine(")");
        }

        builder.AppendLine();
    }

    private static string GetEffectiveColumnName(StaticEntitySeedColumn column)
    {
        return string.IsNullOrWhiteSpace(column.EffectiveColumnName)
            ? column.ColumnName
            : column.EffectiveColumnName;
    }
}

/// <summary>
/// Result of phased MERGE generation.
/// </summary>
public sealed record PhasedInsertScript(
    ImmutableArray<string> PhaseOneInserts,
    ImmutableArray<string> PhaseTwoUpdates,
    bool RequiresPhasing)
{
    public static PhasedInsertScript Empty { get; } = new(
        ImmutableArray<string>.Empty,
        ImmutableArray<string>.Empty,
        false);

    public string ToScript()
    {
        var sb = new StringBuilder();

        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("-- PHASE 1: MERGE with nullable FKs = NULL");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine();

        foreach (var insert in PhaseOneInserts)
        {
            sb.Append(insert);
        }

        if (RequiresPhasing && !PhaseTwoUpdates.IsDefaultOrEmpty)
        {
            sb.AppendLine();
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("-- PHASE 2: UPDATE nullable FKs to actual values");
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine();

            foreach (var update in PhaseTwoUpdates)
            {
                sb.Append(update);
            }
        }

        return sb.ToString();
    }
}

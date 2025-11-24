using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.Domain.Configuration;
using Osm.Domain.Model;

namespace Osm.Emission;

/// <summary>
/// Generates dynamic entity INSERT statements using phased loading strategy
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
    /// Generates phased INSERT scripts for a dataset with circular dependencies.
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
            // No cycles - just emit standard INSERTs
            return GenerateStandardInserts(ordering.Tables, model);
        }

        // Cycles detected - use phased loading
        return GeneratePhasedInserts(ordering.Tables, model, namingOverrides);
    }

    private PhasedInsertScript GenerateStandardInserts(
        ImmutableArray<StaticEntityTableData> tables,
        OsmModel? model)
    {
        var inserts = new List<string>();

        foreach (var table in tables)
        {
            var insertSql = GenerateInsertForTable(table, model, null);
            inserts.Add(insertSql);
        }

        return new PhasedInsertScript(
            PhaseOneInserts: inserts.ToImmutableArray(),
            PhaseTwoUpdates: ImmutableArray<string>.Empty,
            RequiresPhasing: false);
    }

    private PhasedInsertScript GeneratePhasedInserts(
        ImmutableArray<StaticEntityTableData> tables,
        OsmModel? model,
        NamingOverrideOptions namingOverrides)
    {
        var phaseOneInserts = new List<string>();
        var phaseTwoUpdates = new List<string>();

        // Build entity lookup for FK analysis
        var entityLookup = BuildEntityLookup(model);

        foreach (var table in tables)
        {
            if (!entityLookup.TryGetValue(table.Definition.PhysicalName, out var entity))
            {
                // No metadata - emit standard INSERT
                phaseOneInserts.Add(GenerateInsertForTable(table, model, null));
                continue;
            }

            // Identify nullable FKs that should be NULLed in phase 1
            var nullableFKColumns = IdentifyNullableFKColumns(entity, tables, entityLookup);

            // Phase 1: INSERT with nullable FKs = NULL
            phaseOneInserts.Add(GenerateInsertForTable(table, model, nullableFKColumns));

            // Phase 2: UPDATE nullable FKs
            if (nullableFKColumns.Count > 0)
            {
                var updateSql = GenerateUpdateForNullableFKs(table, entity, nullableFKColumns);
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
        ImmutableArray<StaticEntityTableData> tables,
        IReadOnlyDictionary<string, EntityModel> entityLookup)
    {
        var nullableFKColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Get table names in the cycle
        var tablesInCycle = tables.Select(t => t.Definition.PhysicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relationship in entity.Relationships)
        {
            // Only care about relationships with DB constraints
            if (!relationship.HasDatabaseConstraint)
            {
                continue;
            }

            // Only care about relationships to tables in the cycle
            if (!tablesInCycle.Contains(relationship.TargetPhysicalName.Value))
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

    private string GenerateInsertForTable(
        StaticEntityTableData table,
        OsmModel? model,
        HashSet<string>? columnsToNull)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- INSERT: {table.Definition.Schema}.{table.Definition.PhysicalName}");

        if (table.Rows.IsDefaultOrEmpty)
        {
            sb.AppendLine("-- (no rows)");
            return sb.ToString();
        }

        var schema = table.Definition.Schema;
        var tableName = table.Definition.PhysicalName;
        var columns = table.Definition.Columns;

        foreach (var row in table.Rows)
        {
            sb.Append($"INSERT INTO [{schema}].[{tableName}] (");
            sb.Append(string.Join(", ", columns.Select(c => $"[{c.ColumnName}]")));
            sb.AppendLine(") VALUES (");

            var values = new List<string>();
            for (int i = 0; i < columns.Length; i++)
            {
                var column = columns[i];
                var cellValue = i < row.Values.Length ? row.Values[i] : null;

                // If this column should be NULLed in phase 1, emit NULL
                if (columnsToNull != null && columnsToNull.Contains(column.ColumnName))
                {
                    values.Add("NULL");
                }
                else
                {
                    values.Add(_literalFormatter.FormatValue(cellValue));
                }
            }

            sb.AppendLine("  " + string.Join(", ", values));
            sb.AppendLine(");");
        }

        sb.AppendLine("GO");
        sb.AppendLine();

        return sb.ToString();
    }

    private string GenerateUpdateForNullableFKs(
        StaticEntityTableData table,
        EntityModel entity,
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

        // Find PK columns for WHERE clause
        var pkColumns = entity.Attributes
            .Where(a => a.IsIdentifier)
            .Select(a => a.ColumnName.Value)
            .ToList();

        if (pkColumns.Count == 0)
        {
            sb.AppendLine("-- (no PK found - cannot generate UPDATE)");
            return sb.ToString();
        }

        foreach (var row in table.Rows)
        {
            var setClause = new List<string>();
            var whereClause = new List<string>();

            for (int i = 0; i < columns.Length; i++)
            {
                var column = columns[i];
                var cellValue = i < row.Values.Length ? row.Values[i] : null;

                if (pkColumns.Contains(column.ColumnName, StringComparer.OrdinalIgnoreCase))
                {
                    // PK column - use in WHERE clause
                    whereClause.Add($"[{column.ColumnName}] = {_literalFormatter.FormatValue(cellValue)}");
                }
                else if (nullableColumns.Contains(column.ColumnName))
                {
                    // Nullable FK - SET to actual value
                    setClause.Add($"[{column.ColumnName}] = {_literalFormatter.FormatValue(cellValue)}");
                }
            }

            if (setClause.Count > 0 && whereClause.Count > 0)
            {
                sb.AppendLine($"UPDATE [{schema}].[{tableName}]");
                sb.AppendLine($"SET {string.Join(", ", setClause)}");
                sb.AppendLine($"WHERE {string.Join(" AND ", whereClause)};");
            }
        }

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
}

/// <summary>
/// Result of phased INSERT generation.
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
        sb.AppendLine("-- PHASE 1: INSERT with nullable FKs = NULL");
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

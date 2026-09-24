using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Smo;

namespace Osm.Pipeline.Orchestration;

/// <summary>
/// Generates a bootstrap snapshot containing ALL entities (static + regular) in global topological order.
/// This file is used for first-time SSDT deployment to ensure correct FK dependency ordering across module boundaries.
/// </summary>
public sealed class BuildSsdtBootstrapSnapshotStep : IBuildSsdtStep<DynamicInsertsGenerated, BootstrapSnapshotGenerated>
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly StaticSeedSqlBuilder _sqlBuilder;
    private readonly PhasedDynamicEntityInsertGenerator _phasedGenerator;

    public BuildSsdtBootstrapSnapshotStep(
        StaticSeedSqlBuilder sqlBuilder,
        PhasedDynamicEntityInsertGenerator phasedGenerator)
    {
        _sqlBuilder = sqlBuilder ?? throw new ArgumentNullException(nameof(sqlBuilder));
        _phasedGenerator = phasedGenerator ?? throw new ArgumentNullException(nameof(phasedGenerator));
    }

    public async Task<Result<BootstrapSnapshotGenerated>> ExecuteAsync(
        DynamicInsertsGenerated state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var model = state.Bootstrap.FilteredModel
            ?? throw new InvalidOperationException("Pipeline bootstrap step must execute before bootstrap snapshot generation.");

        // Combine static + regular entities
        var staticEntities = state.StaticSeedData.IsDefaultOrEmpty
            ? ImmutableArray<StaticEntityTableData>.Empty
            : state.StaticSeedData;
        var regularEntities = state.Request.DynamicDataset?.Tables ?? ImmutableArray<StaticEntityTableData>.Empty;
        
        // Query supplemental entity data (e.g., ossys_User) from SQL if supplementals are present
        var supplementalEntities = await QuerySupplementalDataAsync(state, cancellationToken).ConfigureAwait(false);
        if (supplementalEntities.IsFailure)
        {
            return Result<BootstrapSnapshotGenerated>.Failure(supplementalEntities.Errors);
        }
        
        var allEntities = staticEntities
            .Concat(regularEntities)
            .Concat(supplementalEntities.Value)
            .ToImmutableArray();

        if (allEntities.Length == 0)
        {
            state.Log.Record(
                "bootstrap.snapshot.skipped",
                "No entities available for bootstrap snapshot (both static and dynamic data are empty).");

            return Result<BootstrapSnapshotGenerated>.Success(new BootstrapSnapshotGenerated(
                state.Request,
                state.Log,
                state.Bootstrap,
                state.EvidenceCache,
                state.Decisions,
                state.Report,
                state.Opportunities,
                state.Validations,
                state.Insights,
                state.Manifest,
                state.DecisionLogPath,
                state.OpportunityArtifacts,
                state.SqlProjectPath,
                state.SqlValidation,
                state.StaticSeedScriptPaths,
                state.StaticSeedData,
                state.DynamicInsertScriptPaths,
                state.DynamicInsertOutputMode,
                state.StaticSeedTopologicalOrderApplied,
                state.StaticSeedOrderingMode,
                state.DynamicInsertTopologicalOrderApplied,
                state.DynamicInsertOrderingMode,
                BootstrapSnapshotPath: null,
                BootstrapTopologicalOrderApplied: false,
                BootstrapOrderingMode: EntityDependencyOrderingMode.Alphabetical,
                BootstrapEntityCount: 0));
        }

        // Apply global topological sort (no module partitioning)
        var sortOptions = state.Request.DeferJunctionTables
            ? new EntityDependencySortOptions(true)
            : EntityDependencySortOptions.Default;

        // Get circular dependency options for manual ordering
        var circularDepsOptions = state.Request.CircularDependencyOptions ?? CircularDependencyOptions.Empty;

        var diagnostics = new List<string>();
        var ordering = EntityDependencySorter.SortByForeignKeys(
            allEntities,
            model,
            state.Request.Scope.SmoOptions.NamingOverrides,
            sortOptions,
            circularDepsOptions,
            diagnostics);

        // Log diagnostics from auto-resolution
        foreach (var diagnostic in diagnostics)
        {
            state.Log.Record(
                "bootstrap.snapshot.auto-resolution-diagnostic",
                diagnostic,
                new PipelineLogMetadataBuilder().Build());
        }

        var orderedEntities = ordering.Tables;

        // Validate topological ordering and extract cycle diagnostics
        var validator = new TopologicalOrderingValidator();
        var validation = validator.Validate(orderedEntities, model, state.Request.Scope.SmoOptions.NamingOverrides, circularDepsOptions);

        if (validation.CycleDetected && validation.SkippedConstraints > 0)
        {
            state.Log.Record(
                "bootstrap.snapshot.cycle-warning",
                "Cycle flag suppressed: foreign key columns not hydrated; run model ingestion with SQL metadata or hydrate constraints.",
                new PipelineLogMetadataBuilder()
                    .WithCount("ordering.constraints.skipped", validation.SkippedConstraints)
                    .WithCount("ordering.constraints.validated", validation.ValidatedConstraints)
                    .Build());
        }

        // Emit cycle diagnostics if detected (even after auto-resolution attempts)
        if (ordering.CycleDetected)
        {
            var hasSccData = ordering.StronglyConnectedComponents.HasValue &&
                             !ordering.StronglyConnectedComponents.Value.IsDefaultOrEmpty;
            
            var cycleDetails = hasSccData
                ? $"{ordering.StronglyConnectedComponents!.Value.Length} strongly connected component(s) detected: " +
                  string.Join("; ", ordering.StronglyConnectedComponents.Value.Select(scc =>
                      $"[{scc.Length} tables: {string.Join(", ", scc.Take(5))}{(scc.Length > 5 ? $" +{scc.Length - 5} more" : "")}]"))
                : "Cycle detected but SCC details not available";

            state.Log.Record(
                "bootstrap.snapshot.cycle-detected",
                ordering.AlphabeticalFallbackApplied
                    ? $"ATTENTION: Circular dependencies detected and auto-resolution failed. Falling back to alphabetical ordering. {cycleDetails}"
                    : $"Circular dependencies detected. {cycleDetails}",
                new PipelineLogMetadataBuilder()
                    .WithCount("ordering.scc.count", hasSccData ? ordering.StronglyConnectedComponents!.Value.Length : 0)
                    .WithValue("ordering.fallback.applied", ordering.AlphabeticalFallbackApplied ? "true" : "false")
                    .Build());
        }

        // Generate bootstrap snapshot script with observability
        var validationOverrides = state.Request.Scope.ModuleFilter.ValidationOverrides;
        var usePhasedLoading = true; // Phased loading validated; enabled by default
        var bootstrapScript = usePhasedLoading
            ? GeneratePhasedBootstrapScript(orderedEntities, ordering, validation, model, state.Request.Scope.SmoOptions.NamingOverrides)
            : GenerateBootstrapScript(orderedEntities, ordering, validation, model, validationOverrides);

        // Write to Bootstrap directory
        var bootstrapDirectory = Path.Combine(state.Request.OutputDirectory, "Bootstrap");
        Directory.CreateDirectory(bootstrapDirectory);
        var bootstrapPath = Path.Combine(bootstrapDirectory, "AllEntitiesIncludingStatic.bootstrap.sql");
        await File.WriteAllTextAsync(bootstrapPath, bootstrapScript, Utf8NoBom, cancellationToken)
            .ConfigureAwait(false);

        // Log metrics with observability
        state.Log.Record(
            "bootstrap.snapshot.generated",
            $"Generated bootstrap snapshot with {orderedEntities.Length} entities in global topological order",
            new PipelineLogMetadataBuilder()
                .WithPath("bootstrap.snapshot.path", bootstrapPath)
                .WithCount("entities.total", orderedEntities.Length)
                .WithCount("entities.static", staticEntities.Length)
                .WithCount("entities.regular", regularEntities.Length)
                .WithCount("entities.supplemental", supplementalEntities.Value.Length)
                .WithValue("ordering.topologicalOrderApplied", ordering.TopologicalOrderingApplied ? "true" : "false")
                .WithValue("ordering.mode", ordering.Mode.ToMetadataValue())
                .WithCount("ordering.nodes", ordering.NodeCount)
                .WithCount("ordering.edges", ordering.EdgeCount)
                .WithValue("ordering.cycleDetected", ordering.CycleDetected ? "true" : "false")
                .Build());

        return Result<BootstrapSnapshotGenerated>.Success(new BootstrapSnapshotGenerated(
            state.Request,
            state.Log,
            state.Bootstrap,
            state.EvidenceCache,
            state.Decisions,
            state.Report,
            state.Opportunities,
            state.Validations,
            state.Insights,
            state.Manifest,
            state.DecisionLogPath,
            state.OpportunityArtifacts,
            state.SqlProjectPath,
            state.SqlValidation,
            state.StaticSeedScriptPaths,
            state.StaticSeedData,
            state.DynamicInsertScriptPaths,
            state.DynamicInsertOutputMode,
            state.StaticSeedTopologicalOrderApplied,
            state.StaticSeedOrderingMode,
            state.DynamicInsertTopologicalOrderApplied,
            state.DynamicInsertOrderingMode,
            BootstrapSnapshotPath: bootstrapPath,
            BootstrapTopologicalOrderApplied: ordering.TopologicalOrderingApplied,
            BootstrapOrderingMode: ordering.Mode,
            BootstrapEntityCount: orderedEntities.Length));
    }

    private string GeneratePhasedBootstrapScript(
        ImmutableArray<StaticEntityTableData> orderedEntities,
        EntityDependencySorter.EntityDependencyOrderingResult ordering,
        TopologicalValidationResult validation,
        OsmModel model,
        NamingOverrideOptions namingOverrides)
    {
        var builder = new StringBuilder();

        // Header with phased loading metadata
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine("-- Bootstrap Snapshot: Phased Loading Strategy (NULL→UPDATE)");
        builder.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine($"-- Total Entities: {orderedEntities.Length}");
        builder.AppendLine($"-- Strategy: Phased loading to resolve circular dependencies");
        builder.AppendLine("--");
        builder.AppendLine("-- Phase 1: INSERT with nullable FKs = NULL (mandatory-edge topological order)");
        builder.AppendLine("-- Phase 2: UPDATE to populate nullable FK values after all tables exist");
        builder.AppendLine("--");
        builder.AppendLine("-- This eliminates the need for constraint disabling while respecting FK integrity.");
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine();

        // Generate phased script using PhasedDynamicEntityInsertGenerator
        var dataset = new DynamicEntityDataset(orderedEntities);
        var phasedScript = _phasedGenerator.Generate(
            dataset,
            model,
            namingOverrides,
            EntityDependencySortOptions.Default,
            CircularDependencyOptions.Empty);

        builder.Append(phasedScript.ToScript());

        // Footer
        builder.AppendLine();
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine($"-- Bootstrap Snapshot Complete: {orderedEntities.Length} entities loaded");
        builder.AppendLine($"-- Phased Loading: {(phasedScript.RequiresPhasing ? "ENABLED" : "NOT REQUIRED")}");
        builder.AppendLine("--------------------------------------------------------------------------------");

        return builder.ToString();
    }

    private string GenerateBootstrapScript(
        ImmutableArray<StaticEntityTableData> orderedEntities,
        EntityDependencySorter.EntityDependencyOrderingResult ordering,
        TopologicalValidationResult validation,
        OsmModel model,
        ModuleValidationOverrides validationOverrides)
    {
        var builder = new StringBuilder();

        // Header with metadata
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine("-- Bootstrap Snapshot: All Entities (Static + Regular)");
        builder.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine($"-- Total Entities: {orderedEntities.Length}");
        builder.AppendLine($"-- Ordering: {(ordering.TopologicalOrderingApplied ? "Global topological order (FK-aware)" : "Alphabetical fallback")}");
        builder.AppendLine($"-- Mode: {ordering.Mode.ToMetadataValue()}");

        // Emit detailed cycle diagnostics if detected
        if (validation.CycleDetected && !validation.Cycles.IsDefaultOrEmpty)
        {
            builder.AppendLine("--");

            var hasAllowedCycles = validation.Cycles.Any(c => c.IsAllowed);
            var hasDisallowedCycles = validation.Cycles.Any(c => !c.IsAllowed);

            if (hasDisallowedCycles)
            {
                builder.AppendLine("-- ⚠️  CIRCULAR DEPENDENCY DETECTED");
            }
            else
            {
                builder.AppendLine("-- ℹ️  ALLOWED CIRCULAR DEPENDENCY (Manual Ordering Applied)");
            }

            builder.AppendLine("--");

            foreach (var cycle in validation.Cycles)
            {
                if (cycle.IsAllowed)
                {
                    builder.AppendLine($"--   ✓ ALLOWED Cycle: {cycle.CyclePath}");
                    builder.AppendLine("--     Manual ordering override active");
                }
                else
                {
                    builder.AppendLine($"--   ⚠️  DISALLOWED Cycle: {cycle.CyclePath}");
                }

                builder.AppendLine("--");
                builder.AppendLine("--   Tables Involved:");
                foreach (var table in cycle.TablesInCycle)
                {
                    builder.AppendLine($"--     - {table}");
                }

                builder.AppendLine("--");
                builder.AppendLine("--   Foreign Keys in Cycle:");
                foreach (var fk in cycle.ForeignKeys)
                {
                    builder.AppendLine($"--     - {fk.ConstraintName}: {fk.SourceTable}.{fk.SourceColumn} → {fk.TargetTable}.{fk.TargetColumn}");
                    builder.AppendLine($"--       Nullable: {(fk.IsNullable ? "YES ✓" : "NO")}");
                    builder.AppendLine($"--       Delete Rule: {fk.DeleteRule}");
                }

                if (!cycle.IsAllowed)
                {
                    builder.AppendLine("--");
                    builder.AppendLine("--   Analysis:");
                    
                    var hasNullableFK = cycle.ForeignKeys.Any(fk => fk.IsNullable);
                    var hasCascade = cycle.ForeignKeys.Any(fk => fk.DeleteRule.Contains("CASCADE", StringComparison.OrdinalIgnoreCase));
                    var hasStrongCycle = cycle.ForeignKeys.Any(fk => !fk.IsNullable || fk.DeleteRule.Contains("CASCADE", StringComparison.OrdinalIgnoreCase));
                    
                    // Count weak vs strong edges
                    var foreignKeysList = cycle.ForeignKeys.ToList();
                    var weakEdgeCount = foreignKeysList.Count(fk => fk.IsNullable && !fk.DeleteRule.Contains("CASCADE", StringComparison.OrdinalIgnoreCase));
                    var strongEdgeCount = foreignKeysList.Count - weakEdgeCount;
                    
                    builder.AppendLine($"--     Weak edges (nullable, non-CASCADE): {weakEdgeCount}");
                    builder.AppendLine($"--     Strong edges (NOT NULL or CASCADE): {strongEdgeCount}");
                    builder.AppendLine("--");
                    
                    if (!hasNullableFK)
                    {
                        builder.AppendLine("--   Recommendation:");
                        builder.AppendLine("--     ⚠️  All FKs are NOT NULL - circular reference cannot be auto-resolved");
                        builder.AppendLine("--     Action Required: Make at least one FK nullable or provide manual cycle ordering");
                    }
                    else if (hasStrongCycle && cycle.TablesInCycle.Count() > 2)
                    {
                        builder.AppendLine("--   Recommendation:");
                        builder.AppendLine("--     ⚠️  Cycle contains strong edges (NOT NULL or CASCADE) that form an unbreakable loop");
                        builder.AppendLine("--     Auto-resolution failed: Alphabetical fallback applied - FK order not guaranteed");
                        if (hasCascade)
                        {
                            builder.AppendLine("--     🚨 CASCADE deletes in cycle - phased loading strategy may cause data loss!");
                        }
                        builder.AppendLine("--     Action Required: Provide manual cycle ordering via CircularDependencyOptions");
                    }
                    else
                    {
                        builder.AppendLine("--   Recommendation:");
                        var nullableFKs = cycle.ForeignKeys.Where(fk => fk.IsNullable).Select(fk => fk.ConstraintName);
                        builder.AppendLine($"--     Cycle can be handled with phased loading (nullable FKs: {string.Join(", ", nullableFKs)})");
                        builder.AppendLine("--     Strategy: INSERT with NULL → INSERT dependents → UPDATE with FK values");
                        if (hasCascade)
                        {
                            builder.AppendLine("--     🚨 WARNING: CASCADE delete detected - verify phased loading strategy!");
                        }
                    }
                }

                builder.AppendLine("--");
            }

            if (!hasAllowedCycles)
            {
                builder.AppendLine("--   Note: Alphabetical fallback ordering applied - FK order not guaranteed");
            }
            else
            {
                builder.AppendLine("--   Note: Manual ordering applied from CircularDependencyOptions configuration");
            }
        }
        else if (ordering.CycleDetected)
        {
            builder.AppendLine("-- WARNING: Circular dependencies detected - alphabetical fallback applied");
        }

        builder.AppendLine("--");
        builder.AppendLine("-- USAGE: This file is applied ONCE on first SSDT deployment via PostDeployment guard.");
        builder.AppendLine("--        Do NOT commit to source control (add Bootstrap/ to .gitignore).");
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine();

        // Generate MERGE script for each entity with observability
        for (var i = 0; i < orderedEntities.Length; i++)
        {
            var entity = orderedEntities[i];
            var definition = entity.Definition;

            // Topological position comment
            builder.AppendLine($"-- Entity: {definition.LogicalName} ({definition.Schema}.{definition.PhysicalName})");
            builder.AppendLine($"-- Module: {definition.Module}");
            builder.AppendLine($"-- Topological Order: {i + 1} of {orderedEntities.Length}");
            builder.AppendLine();

            // Generate MERGE statement (NonDestructive mode for idempotent bootstrap on fresh database)
            var insertScript = _sqlBuilder.BuildBlock(entity, StaticSeedSynchronizationMode.NonDestructive, validationOverrides);
            builder.AppendLine(insertScript);

            // Diagnostic PRINT statement
            builder.AppendLine($"PRINT 'Bootstrap: Completed entity {i + 1}/{orderedEntities.Length}: {definition.Schema}.{definition.EffectiveName} ({entity.Rows.Length} rows)';");
            builder.AppendLine("GO");
            builder.AppendLine();
        }

        // Footer
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine($"-- Bootstrap Snapshot Complete: {orderedEntities.Length} entities loaded");
        builder.AppendLine("--------------------------------------------------------------------------------");

        return builder.ToString();
    }

    private async Task<Result<ImmutableArray<StaticEntityTableData>>> QuerySupplementalDataAsync(
        DynamicInsertsGenerated state,
        CancellationToken cancellationToken)
    {
        var supplementalEntities = state.Bootstrap.SupplementalEntities;
        if (supplementalEntities.IsDefaultOrEmpty)
        {
            return Result<ImmutableArray<StaticEntityTableData>>.Success(ImmutableArray<StaticEntityTableData>.Empty);
        }

        // Build table definitions for supplementals
        var definitions = new List<StaticEntitySeedTableDefinition>();
        foreach (var entity in supplementalEntities)
        {
            var columns = entity.Attributes
                .Select(attr => new StaticEntitySeedColumn(
                    attr.LogicalName.Value,
                    attr.ColumnName.Value,
                    attr.ColumnName.Value, // Use physical column name for emission
                    attr.DataType,
                    attr.Length,
                    attr.Precision,
                    attr.Scale,
                    attr.IsIdentifier,
                    attr.OnDisk?.IsIdentity ?? false,
                    attr.OnDisk?.IsNullable ?? !attr.IsMandatory))
                .ToImmutableArray();

            definitions.Add(new StaticEntitySeedTableDefinition(
                entity.Module.Value,
                entity.LogicalName.Value,
                entity.Schema.Value,
                entity.PhysicalName.Value, // Physical name for querying from SQL
                entity.LogicalName.Value, // Logical name for emission (dbo.User not dbo.ossys_User)
                columns));
        }

        if (definitions.Count == 0)
        {
            return Result<ImmutableArray<StaticEntityTableData>>.Success(ImmutableArray<StaticEntityTableData>.Empty);
        }

        // Use the static data provider to query supplemental data from SQL
        var dataProvider = state.Request.StaticDataProvider;
        if (dataProvider is null)
        {
            state.Log.Record(
                "bootstrap.supplemental.skipped",
                $"Skipping supplemental data extraction - no static data provider available. Supplementals will be DDL-only (no INSERT statements).");

            return Result<ImmutableArray<StaticEntityTableData>>.Success(ImmutableArray<StaticEntityTableData>.Empty);
        }

        var dataResult = await dataProvider.GetDataAsync(definitions, cancellationToken).ConfigureAwait(false);
        if (dataResult.IsFailure)
        {
            if (dataResult.Errors.All(error => string.Equals(error.Code, "cli.staticData.fixture.tableMissing", StringComparison.Ordinal)))
            {
                state.Log.Record(
                    "bootstrap.supplemental.missingFixture",
                    "Supplemental fixture data missing; continuing without supplemental rows.");
                return Result<ImmutableArray<StaticEntityTableData>>.Success(ImmutableArray<StaticEntityTableData>.Empty);
            }

            return Result<ImmutableArray<StaticEntityTableData>>.Failure(dataResult.Errors);
        }

        return Result<ImmutableArray<StaticEntityTableData>>.Success(dataResult.Value.ToImmutableArray());
    }
}

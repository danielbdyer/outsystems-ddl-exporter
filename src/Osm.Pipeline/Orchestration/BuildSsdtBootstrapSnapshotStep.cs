using System;
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
using Osm.Emission.Seeds;
using Osm.Smo;

namespace Osm.Pipeline.Orchestration;

/// <summary>
/// M1.0: Generates a bootstrap snapshot containing ALL entities (static + regular) in global topological order.
/// This file is used for first-time SSDT deployment to ensure correct FK dependency ordering across module boundaries.
/// </summary>
public sealed class BuildSsdtBootstrapSnapshotStep : IBuildSsdtStep<DynamicInsertsGenerated, BootstrapSnapshotGenerated>
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly StaticSeedSqlBuilder _sqlBuilder;

    public BuildSsdtBootstrapSnapshotStep(StaticSeedSqlBuilder sqlBuilder)
    {
        _sqlBuilder = sqlBuilder ?? throw new ArgumentNullException(nameof(sqlBuilder));
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

        // Combine static + regular entities (as per M1.0 spec)
        var staticEntities = state.StaticSeedData.IsDefaultOrEmpty
            ? ImmutableArray<StaticEntityTableData>.Empty
            : state.StaticSeedData;
        var regularEntities = state.Request.DynamicDataset?.Tables ?? ImmutableArray<StaticEntityTableData>.Empty;
        var allEntities = staticEntities.Concat(regularEntities).ToImmutableArray();

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

        // Apply global topological sort (no module partitioning - critical for M1.0)
        var sortOptions = state.Request.DeferJunctionTables
            ? new EntityDependencySortOptions(true)
            : EntityDependencySortOptions.Default;

        var ordering = EntityDependencySorter.SortByForeignKeys(
            allEntities,
            model,
            state.Request.Scope.SmoOptions.NamingOverrides,
            sortOptions);

        var orderedEntities = ordering.Tables;

        // Generate bootstrap snapshot script with observability
        var bootstrapScript = GenerateBootstrapScript(orderedEntities, ordering, model);

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

    private string GenerateBootstrapScript(
        ImmutableArray<StaticEntityTableData> orderedEntities,
        EntityDependencySorter.EntityDependencyOrderingResult ordering,
        OsmModel model)
    {
        var builder = new StringBuilder();

        // Header with metadata (M1.0 observability requirement)
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine("-- M1.0 Bootstrap Snapshot: All Entities (Static + Regular)");
        builder.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine($"-- Total Entities: {orderedEntities.Length}");
        builder.AppendLine($"-- Ordering: {(ordering.TopologicalOrderingApplied ? "Global topological order (FK-aware)" : "Alphabetical fallback")}");
        builder.AppendLine($"-- Mode: {ordering.Mode.ToMetadataValue()}");

        if (ordering.CycleDetected)
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

            // Topological position comment (M1.0 observability requirement)
            builder.AppendLine($"-- Entity: {definition.LogicalName} ({definition.Schema}.{definition.PhysicalName})");
            builder.AppendLine($"-- Module: {definition.Module}");
            builder.AppendLine($"-- Topological Order: {i + 1} of {orderedEntities.Length}");
            builder.AppendLine();

            // Generate MERGE statement (reuse existing StaticSeedSqlBuilder)
            var mergeScript = _sqlBuilder.BuildBlock(entity, StaticSeedSynchronizationMode.ValidateThenApply);
            builder.AppendLine(mergeScript);

            // Diagnostic PRINT statement (M1.0 observability requirement)
            builder.AppendLine($"PRINT '[M1.0] Bootstrap: Completed entity {i + 1}/{orderedEntities.Length}: {definition.Schema}.{definition.PhysicalName} ({entity.Rows.Length} rows)';");
            builder.AppendLine("GO");
            builder.AppendLine();
        }

        // Footer
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine($"-- M1.0 Bootstrap Snapshot Complete: {orderedEntities.Length} entities loaded");
        builder.AppendLine("--------------------------------------------------------------------------------");

        return builder.ToString();
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Smo;
using Osm.Pipeline.DynamicData;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtDynamicInsertStep : IBuildSsdtStep<StaticSeedsGenerated, DynamicInsertsGenerated>
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly DynamicEntityInsertGenerator _generator;

    public BuildSsdtDynamicInsertStep(DynamicEntityInsertGenerator generator)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    public async Task<Result<DynamicInsertsGenerated>> ExecuteAsync(
        StaticSeedsGenerated state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var dataset = state.Request.DynamicDataset ?? DynamicEntityDataset.Empty;
        var datasetSource = state.Request.DynamicDatasetSource;
        var tableCount = dataset.Tables.IsDefaultOrEmpty ? 0 : dataset.Tables.Length;
        var totalRows = dataset.Tables.IsDefaultOrEmpty ? 0 : dataset.Tables.Sum(static table => table.Rows.Length);

        state.Log.Record(
            "dynamicData.dataset.summary",
            BuildDatasetSummaryMessage(datasetSource, tableCount, totalRows, dataset.IsEmpty),
            new PipelineLogMetadataBuilder()
                .WithValue("dataset.source", datasetSource.ToString())
                .WithCount("tables", tableCount)
                .WithCount("rows", totalRows)
                .Build());

        if (dataset.IsEmpty)
        {
            state.Log.Record(
                "dynamicData.insert.skipped",
                "Dynamic dataset did not contain rows. INSERT scripts were not generated.");

            return Result<DynamicInsertsGenerated>.Success(new DynamicInsertsGenerated(
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
                ImmutableArray<string>.Empty,
                state.Request.DynamicInsertOutputMode,
                state.StaticSeedTopologicalOrderApplied,
                state.StaticSeedOrderingMode,
                DynamicInsertTopologicalOrderApplied: false,
                DynamicInsertOrderingMode: EntityDependencyOrderingMode.Alphabetical));
        }

        var namingOverrides = state.Request.Scope.SmoOptions.NamingOverrides ?? NamingOverrideOptions.Empty;
        var sortOptions = state.Request.DeferJunctionTables
            ? new EntityDependencySortOptions(true)
            : EntityDependencySortOptions.Default;
        var ordering = EntityDependencySorter.SortByForeignKeys(
            dataset.Tables,
            state.Bootstrap.FilteredModel,
            namingOverrides,
            sortOptions);

        var artifacts = _generator.GenerateArtifacts(
            dataset,
            state.StaticSeedData,
            model: state.Bootstrap.FilteredModel,
            namingOverrides: namingOverrides,
            sortOptions: sortOptions);

        var dynamicOrderApplied = ordering.TopologicalOrderingApplied;
        var dynamicOrderingMode = ordering.Mode;
        if (artifacts.IsDefaultOrEmpty || artifacts.Length == 0)
        {
            state.Log.Record(
                "dynamicData.insert.skipped",
                "Dynamic dataset did not contain any unique rows after deduplication. No INSERT scripts emitted.");

            return Result<DynamicInsertsGenerated>.Success(new DynamicInsertsGenerated(
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
                ImmutableArray<string>.Empty,
                state.Request.DynamicInsertOutputMode,
                state.StaticSeedTopologicalOrderApplied,
                state.StaticSeedOrderingMode,
                DynamicInsertTopologicalOrderApplied: dynamicOrderApplied,
                DynamicInsertOrderingMode: dynamicOrderingMode));
        }

        var outputRoot = state.Request.DynamicDataOutputDirectoryHint;
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            outputRoot = Path.Combine(state.Request.OutputDirectory, "DynamicData");
        }

        Directory.CreateDirectory(outputRoot!);

        ImmutableArray<string> scriptPaths = state.Request.DynamicInsertOutputMode switch
        {
            DynamicInsertOutputMode.PerEntity => await WritePerEntityScriptsAsync(
                outputRoot!,
                artifacts,
                state.Request.Scope.SmoOptions.SanitizeModuleNames,
                state.Log,
                cancellationToken).ConfigureAwait(false),
            DynamicInsertOutputMode.SingleFile => ImmutableArray.Create(
                await WriteSingleFileScriptAsync(outputRoot!, artifacts, cancellationToken).ConfigureAwait(false)),
            _ => throw new InvalidOperationException($"Unsupported dynamic insert output mode: {state.Request.DynamicInsertOutputMode}.")
        };

        state.Log.Record(
            "dynamicData.insert.generated",
            "Generated dynamic entity INSERT scripts.",
            new PipelineLogMetadataBuilder()
                .WithValue(
                    "outputs.dynamicInsertPaths",
                    scriptPaths.IsDefaultOrEmpty || scriptPaths.Length == 0 ? string.Empty : string.Join(";", scriptPaths))
                .WithValue("outputs.dynamicInsertMode", state.Request.DynamicInsertOutputMode.ToString())
                .WithCount("tables", artifacts.Length)
                .WithCount("ordering.nodes", ordering.NodeCount)
                .WithCount("ordering.edges", ordering.EdgeCount)
                .WithCount("ordering.missingEdges", ordering.MissingEdgeCount)
                .WithValue("ordering.cycleDetected", ordering.CycleDetected ? "true" : "false")
                .WithValue("ordering.fallbackApplied", ordering.AlphabeticalFallbackApplied ? "true" : "false")
                .WithValue("ordering.mode", ordering.Mode.ToMetadataValue())
                .Build());

        return Result<DynamicInsertsGenerated>.Success(new DynamicInsertsGenerated(
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
            scriptPaths,
            state.Request.DynamicInsertOutputMode,
            state.StaticSeedTopologicalOrderApplied,
            state.StaticSeedOrderingMode,
            DynamicInsertTopologicalOrderApplied: dynamicOrderApplied,
            DynamicInsertOrderingMode: dynamicOrderingMode));
    }

    private static async Task<ImmutableArray<string>> WritePerEntityScriptsAsync(
        string outputRoot,
        ImmutableArray<DynamicEntityInsertArtifact> artifacts,
        bool sanitizeModules,
        PipelineExecutionLogBuilder log,
        CancellationToken cancellationToken)
    {
        var recordedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scriptPaths = ImmutableArray.CreateBuilder<string>(artifacts.Length);

        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var moduleName = artifact.Definition.Module ?? "<unknown>";
            var sanitized = sanitizeModules ? ModuleNameSanitizer.Sanitize(moduleName) : moduleName;
            var directoryName = ResolveModuleDirectoryName(
                moduleName,
                sanitized,
                recordedModules,
                log,
                "dynamicData.insert.moduleNameRemapped");

            var moduleDirectory = Path.Combine(outputRoot, directoryName);
            Directory.CreateDirectory(moduleDirectory);

            var fileName = $"{artifact.Definition.PhysicalName}.dynamic.sql";
            var filePath = Path.Combine(moduleDirectory, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await using var writer = new StreamWriter(stream, Utf8NoBom);
            await artifact.WriteAsync(writer, cancellationToken).ConfigureAwait(false);

            scriptPaths.Add(filePath);
        }

        return scriptPaths.MoveToImmutable();
    }

    private static string ResolveModuleDirectoryName(
        string moduleName,
        string? sanitizedName,
        ISet<string> recordedModules,
        PipelineExecutionLogBuilder log,
        string logStep)
    {
        var candidate = string.IsNullOrWhiteSpace(sanitizedName) ? moduleName : sanitizedName;
        var directoryName = candidate;
        if (recordedModules.Add(directoryName))
        {
            return directoryName;
        }

        var suffix = 2;
        while (!recordedModules.Add(directoryName = $"{candidate}_{suffix}"))
        {
            suffix++;
        }

        log.Record(
            logStep,
            $"Module '{moduleName}' collided with another module directory. Remapped to '{directoryName}'.",
            new PipelineLogMetadataBuilder()
                .WithValue("module.originalName", moduleName)
                .WithValue("module.sanitizedName", candidate)
                .WithValue("module.disambiguatedName", directoryName)
                .Build());

        return directoryName;
    }

    private static async Task<string> WriteSingleFileScriptAsync(
        string outputRoot,
        ImmutableArray<DynamicEntityInsertArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputRoot, "DynamicData.all.dynamic.sql");

        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using var writer = new StreamWriter(stream, Utf8NoBom);

        await writer.WriteLineAsync("--------------------------------------------------------------------------------").ConfigureAwait(false);
        await writer.WriteLineAsync("-- Consolidated dynamic entity INSERT replay script").ConfigureAwait(false);
        await writer.WriteLineAsync($"-- Tables: {artifacts.Length}").ConfigureAwait(false);
        await writer.WriteLineAsync("--------------------------------------------------------------------------------").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);

        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await artifact.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        return filePath;
    }

    private static string BuildDatasetSummaryMessage(
        DynamicDatasetSource source,
        int tableCount,
        int totalRows,
        bool isEmpty)
    {
        var sourceDescription = source switch
        {
            DynamicDatasetSource.UserProvided => "user-provided",
            DynamicDatasetSource.Extraction => "extraction",
            DynamicDatasetSource.SqlProvider => "SQL provider",
            _ => "unspecified"
        };

        if (isEmpty)
        {
            return $"Dynamic dataset ({sourceDescription}) did not yield any rows.";
        }

        return $"Dynamic dataset ({sourceDescription}) contains {totalRows} row(s) across {tableCount} table(s).";
    }
}

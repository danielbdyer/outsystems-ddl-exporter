using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Emission.Seeds;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtStaticSeedStep : IBuildSsdtStep<SqlValidated, StaticSeedsGenerated>
{
    private readonly StaticEntitySeedScriptGenerator _seedGenerator;

    public BuildSsdtStaticSeedStep(
        StaticEntitySeedScriptGenerator seedGenerator)
    {
        _seedGenerator = seedGenerator ?? throw new ArgumentNullException(nameof(seedGenerator));
    }

    public async Task<Result<StaticSeedsGenerated>> ExecuteAsync(
        SqlValidated state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var model = state.Bootstrap.FilteredModel
            ?? throw new InvalidOperationException("Pipeline bootstrap step must execute before static seed generation.");
        var seedDefinitions = StaticEntitySeedDefinitionBuilder.Build(model, state.Request.Scope.SmoOptions.NamingOverrides);
        if (seedDefinitions.IsDefaultOrEmpty)
        {
            state.Log.Record(
                "staticData.seed.skipped",
                "No static entity seeds required for request.");
            return Result<StaticSeedsGenerated>.Success(new StaticSeedsGenerated(
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
                ImmutableArray<string>.Empty,
                ImmutableArray<StaticEntityTableData>.Empty,
                StaticSeedTopologicalOrderApplied: false));
        }

        if (state.Request.StaticDataProvider is null)
        {
            return Result<StaticSeedsGenerated>.Failure(ValidationError.Create(
                "pipeline.buildSsdt.staticData.missingProvider",
                "Static entity data provider is required when the model includes static entities."));
        }

        var staticDataResult = await state.Request.StaticDataProvider
            .GetDataAsync(seedDefinitions, cancellationToken)
            .ConfigureAwait(false);
        if (staticDataResult.IsFailure)
        {
            return Result<StaticSeedsGenerated>.Failure(staticDataResult.Errors);
        }

        var deterministicData = EntitySeedDeterminizer.Normalize(staticDataResult.Value);
        var ordering = EntityDependencySorter.SortByForeignKeys(deterministicData, model);
        var orderedData = ordering.Tables;
        var topologicalOrderingApplied = ordering.TopologicalOrderingApplied;
        var preflight = StaticSeedForeignKeyPreflight.Analyze(orderedData, model);
        LogForeignKeyPreflight(state.Log, preflight);
        var seedOptions = state.Request.Scope.TighteningOptions.Emission.StaticSeeds;
        var seedsRoot = state.Request.SeedOutputDirectoryHint;
        if (string.IsNullOrWhiteSpace(seedsRoot))
        {
            seedsRoot = Path.Combine(state.Request.OutputDirectory, "Seeds");
        }

        Directory.CreateDirectory(seedsRoot!);
        var seedPathBuilder = ImmutableArray.CreateBuilder<string>();

        if (seedOptions.GroupByModule)
        {
            var modules = orderedData
                .Select(table => table.Definition.Module)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var usedModuleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var moduleName in modules)
            {
                var sanitizedModule = state.Request.Scope.SmoOptions.SanitizeModuleNames
                    ? ModuleNameSanitizer.Sanitize(moduleName)
                    : moduleName;

                var moduleDirectoryName = ResolveModuleDirectoryName(
                    moduleName,
                    sanitizedModule,
                    usedModuleNames,
                    state.Log,
                    "staticData.seed.moduleNameRemapped");

                var moduleDirectory = Path.Combine(seedsRoot!, moduleDirectoryName);
                Directory.CreateDirectory(moduleDirectory);

                var moduleTables = orderedData
                    .Where(table => string.Equals(table.Definition.Module, moduleName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                var modulePath = Path.Combine(moduleDirectory, "StaticEntities.seed.sql");
                await _seedGenerator
                    .WriteAsync(modulePath, moduleTables, seedOptions.SynchronizationMode, model, cancellationToken)
                    .ConfigureAwait(false);
                seedPathBuilder.Add(modulePath);
            }

            if (seedOptions.EmitMasterFile)
            {
                var masterPath = Path.Combine(seedsRoot!, "StaticEntities.seed.sql");
                await _seedGenerator
                    .WriteAsync(masterPath, orderedData, seedOptions.SynchronizationMode, model, cancellationToken)
                    .ConfigureAwait(false);
                seedPathBuilder.Add(masterPath);
            }
        }
        else
        {
            var seedPath = Path.Combine(seedsRoot!, "StaticEntities.seed.sql");
            await _seedGenerator
                .WriteAsync(seedPath, orderedData, seedOptions.SynchronizationMode, model, cancellationToken)
                .ConfigureAwait(false);
            seedPathBuilder.Add(seedPath);
        }

        var seedPaths = seedPathBuilder.ToImmutable();
        state.Log.Record(
            "staticData.seed.generated",
            "Generated static entity seed scripts.",
            new PipelineLogMetadataBuilder()
                .WithValue(
                    "outputs.seedPaths",
                    seedPaths.IsDefaultOrEmpty ? string.Empty : string.Join(";", seedPaths))
                .WithCount("tables", seedDefinitions.Length)
                .WithCount("ordering.nodes", ordering.NodeCount)
                .WithCount("ordering.edges", ordering.EdgeCount)
                .WithCount("ordering.missingEdges", ordering.MissingEdgeCount)
                .WithValue("ordering.cycleDetected", ordering.CycleDetected ? "true" : "false")
                .WithValue("ordering.fallbackApplied", ordering.AlphabeticalFallbackApplied ? "true" : "false")
                .WithValue("ordering.mode", ordering.TopologicalOrderingApplied ? "topological" : "alphabetical")
                .Build());

        return Result<StaticSeedsGenerated>.Success(new StaticSeedsGenerated(
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
            seedPaths,
            orderedData,
            topologicalOrderingApplied));
    }

    private static string ResolveModuleDirectoryName(
        string moduleName,
        string sanitizedModule,
        ISet<string> usedModuleNames,
        PipelineExecutionLogBuilder log,
        string logStep)
    {
        var candidate = sanitizedModule;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = moduleName;
        }

        var directoryName = candidate;
        if (usedModuleNames.Add(directoryName))
        {
            return directoryName;
        }

        var suffix = 2;
        while (!usedModuleNames.Add(directoryName = $"{candidate}_{suffix}"))
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

    private static void LogForeignKeyPreflight(
        PipelineExecutionLogBuilder log,
        StaticSeedForeignKeyPreflightResult result)
    {
        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var metadataBuilder = new PipelineLogMetadataBuilder()
            .WithCount("foreignKeys.orphans", result.MissingParents.Length)
            .WithCount("foreignKeys.orderingViolations", result.OrderingViolations.Length);

        if (!result.MissingParents.IsDefaultOrEmpty)
        {
            metadataBuilder.WithValue("foreignKeys.orphans.sample", FormatIssues(result.MissingParents));
        }

        if (!result.OrderingViolations.IsDefaultOrEmpty)
        {
            metadataBuilder.WithValue("foreignKeys.ordering.sample", FormatIssues(result.OrderingViolations));
        }

        log.Record(
            "staticData.seed.preflight",
            result.HasFindings
                ? "Static seed FK preflight detected anomalies."
                : "Static seed FK preflight completed without anomalies.",
            metadataBuilder.Build());
    }

    private static string FormatIssues(ImmutableArray<StaticSeedForeignKeyIssue> issues)
    {
        if (issues.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        return string.Join(
            ";",
            issues
                .Take(5)
                .Select(issue => FormattableString.Invariant(
                    $"{issue.ChildSchema}.{issue.ChildTable}->{issue.ReferencedSchema}.{issue.ReferencedTable}({FormatConstraintName(issue.ConstraintName)})")));
    }

    private static string FormatConstraintName(string constraintName)
        => string.IsNullOrWhiteSpace(constraintName) ? "unnamed" : constraintName;
}

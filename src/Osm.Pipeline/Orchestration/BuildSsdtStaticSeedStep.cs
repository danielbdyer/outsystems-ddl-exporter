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

        var deterministicData = StaticEntitySeedDeterminizer.Normalize(staticDataResult.Value);
        var orderedData = EntityDependencySorter.SortByForeignKeys(deterministicData, model);
        var topologicalOrderingApplied = model is not null && !orderedData.IsDefaultOrEmpty;
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

                var moduleDirectoryName = sanitizedModule;
                if (!usedModuleNames.Add(moduleDirectoryName))
                {
                    var suffix = 2;
                    while (!usedModuleNames.Add(moduleDirectoryName = $"{sanitizedModule}_{suffix}"))
                    {
                        suffix++;
                    }

                    if (state.Request.Scope.SmoOptions.SanitizeModuleNames)
                    {
                        state.Log.Record(
                            "staticData.seed.moduleNameRemapped",
                            $"Sanitized module name '{sanitizedModule}' for module '{moduleName}' collided with another module. Remapped to '{moduleDirectoryName}'.",
                            new PipelineLogMetadataBuilder()
                                .WithValue("module.originalName", moduleName)
                                .WithValue("module.sanitizedName", sanitizedModule)
                                .WithValue("module.disambiguatedName", moduleDirectoryName)
                                .Build());
                    }
                }

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
}

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
        var seedDefinitions = StaticEntitySeedDefinitionBuilder.Build(model, state.Request.SmoOptions.NamingOverrides);
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
                state.Insights,
                state.Manifest,
                state.DecisionLogPath,
                state.OpportunityArtifacts,
                state.SqlValidation,
                ImmutableArray<string>.Empty));
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
        var seedOptions = state.Request.TighteningOptions.Emission.StaticSeeds;
        var seedsRoot = state.Request.SeedOutputDirectoryHint;
        if (string.IsNullOrWhiteSpace(seedsRoot))
        {
            seedsRoot = Path.Combine(state.Request.OutputDirectory, "Seeds");
        }

        Directory.CreateDirectory(seedsRoot!);
        var seedPathBuilder = ImmutableArray.CreateBuilder<string>();

        if (seedOptions.GroupByModule)
        {
            var grouped = deterministicData
                .GroupBy(table => table.Definition.Module, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            var usedModuleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in grouped)
            {
                var sanitizedModule = state.Request.SmoOptions.SanitizeModuleNames
                    ? ModuleNameSanitizer.Sanitize(group.Key)
                    : group.Key;

                var moduleDirectoryName = sanitizedModule;
                if (!usedModuleNames.Add(moduleDirectoryName))
                {
                    var suffix = 2;
                    while (!usedModuleNames.Add(moduleDirectoryName = $"{sanitizedModule}_{suffix}"))
                    {
                        suffix++;
                    }

                    if (state.Request.SmoOptions.SanitizeModuleNames)
                    {
                        state.Log.Record(
                            "staticData.seed.moduleNameRemapped",
                            $"Sanitized module name '{sanitizedModule}' for module '{group.Key}' collided with another module. Remapped to '{moduleDirectoryName}'.",
                            new PipelineLogMetadataBuilder()
                                .WithValue("module.originalName", group.Key)
                                .WithValue("module.sanitizedName", sanitizedModule)
                                .WithValue("module.disambiguatedName", moduleDirectoryName)
                                .Build());
                    }
                }

                var moduleDirectory = Path.Combine(seedsRoot!, moduleDirectoryName);
                Directory.CreateDirectory(moduleDirectory);

                var moduleTables = group
                    .OrderBy(table => table.Definition.LogicalName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(table => table.Definition.EffectiveName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var modulePath = Path.Combine(moduleDirectory, "StaticEntities.seed.sql");
                await _seedGenerator
                    .WriteAsync(modulePath, moduleTables, seedOptions.SynchronizationMode, cancellationToken)
                    .ConfigureAwait(false);
                seedPathBuilder.Add(modulePath);
            }
        }
        else
        {
            var seedPath = Path.Combine(seedsRoot!, "StaticEntities.seed.sql");
            await _seedGenerator
                .WriteAsync(seedPath, deterministicData, seedOptions.SynchronizationMode, cancellationToken)
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
            state.Insights,
                state.Manifest,
                state.DecisionLogPath,
                state.OpportunityArtifacts,
                state.SqlValidation,
                seedPaths));
    }
}

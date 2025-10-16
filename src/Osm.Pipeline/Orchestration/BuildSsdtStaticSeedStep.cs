using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Emission.Seeds;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtStaticSeedStep : IBuildSsdtStep
{
    private readonly StaticEntitySeedScriptGenerator _seedGenerator;
    private readonly StaticEntitySeedTemplate _seedTemplate;

    public BuildSsdtStaticSeedStep(
        StaticEntitySeedScriptGenerator seedGenerator,
        StaticEntitySeedTemplate seedTemplate)
    {
        _seedGenerator = seedGenerator ?? throw new ArgumentNullException(nameof(seedGenerator));
        _seedTemplate = seedTemplate ?? throw new ArgumentNullException(nameof(seedTemplate));
    }

    public async Task<Result<BuildSsdtPipelineContext>> ExecuteAsync(
        BuildSsdtPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var model = context.FilteredModel ?? throw new InvalidOperationException("Pipeline bootstrap step must execute before static seed generation.");
        var seedDefinitions = StaticEntitySeedDefinitionBuilder.Build(model, context.Request.SmoOptions.NamingOverrides);
        if (seedDefinitions.IsDefaultOrEmpty)
        {
            context.SetStaticSeedScriptPaths(ImmutableArray<string>.Empty);
            context.Log.Record(
                "staticData.seed.skipped",
                "No static entity seeds required for request.");
            return Result<BuildSsdtPipelineContext>.Success(context);
        }

        if (context.Request.StaticDataProvider is null)
        {
            return Result<BuildSsdtPipelineContext>.Failure(ValidationError.Create(
                "pipeline.buildSsdt.staticData.missingProvider",
                "Static entity data provider is required when the model includes static entities."));
        }

        var staticDataResult = await context.Request.StaticDataProvider
            .GetDataAsync(seedDefinitions, cancellationToken)
            .ConfigureAwait(false);
        if (staticDataResult.IsFailure)
        {
            return Result<BuildSsdtPipelineContext>.Failure(staticDataResult.Errors);
        }

        var deterministicData = StaticEntitySeedDeterminizer.Normalize(staticDataResult.Value);
        var seedOptions = context.Request.TighteningOptions.Emission.StaticSeeds;
        var seedsRoot = context.Request.SeedOutputDirectoryHint;
        if (string.IsNullOrWhiteSpace(seedsRoot))
        {
            seedsRoot = Path.Combine(context.Request.OutputDirectory, "Seeds");
        }

        Directory.CreateDirectory(seedsRoot!);
        var seedPathBuilder = ImmutableArray.CreateBuilder<string>();

        if (seedOptions.GroupByModule)
        {
            var grouped = deterministicData
                .GroupBy(table => table.Definition.Module, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in grouped)
            {
                var sanitizedModule = context.Request.SmoOptions.SanitizeModuleNames
                    ? ModuleNameSanitizer.Sanitize(group.Key)
                    : group.Key;

                var moduleDirectory = Path.Combine(seedsRoot!, sanitizedModule);
                Directory.CreateDirectory(moduleDirectory);

                var moduleTables = group
                    .OrderBy(table => table.Definition.LogicalName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(table => table.Definition.EffectiveName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var modulePath = Path.Combine(moduleDirectory, "StaticEntities.seed.sql");
                await _seedGenerator
                    .WriteAsync(modulePath, _seedTemplate, moduleTables, seedOptions.SynchronizationMode, cancellationToken)
                    .ConfigureAwait(false);
                seedPathBuilder.Add(modulePath);
            }
        }
        else
        {
            var seedPath = Path.Combine(seedsRoot!, "StaticEntities.seed.sql");
            await _seedGenerator
                .WriteAsync(seedPath, _seedTemplate, deterministicData, seedOptions.SynchronizationMode, cancellationToken)
                .ConfigureAwait(false);
            seedPathBuilder.Add(seedPath);
        }

        var seedPaths = seedPathBuilder.ToImmutable();
        context.SetStaticSeedScriptPaths(seedPaths);
        context.Log.Record(
            "staticData.seed.generated",
            "Generated static entity seed scripts.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["paths"] = seedPaths.IsDefaultOrEmpty ? string.Empty : string.Join(";", seedPaths),
                ["tableCount"] = seedDefinitions.Length.ToString(CultureInfo.InvariantCulture)
            });

        return Result<BuildSsdtPipelineContext>.Success(context);
    }
}
